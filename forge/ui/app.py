"""
Forge 메인 GUI.

실행 시:
  1. stdlib tkinter/ttk 로 독립 윈도우 생성 (Windows 'vista' 테마)
  2. 한/글 COM 인스턴스 자동 attach (또는 신규 생성)
  3. 3-탭 노트북 (개별 작업 / 기본정보 / 마크다운 입력)
  4. 상단에 한/글 연결 상태 + About(?) 버튼

진입: `python -m forge.ui.app` 또는 `python run.py`
"""
from __future__ import annotations

import sys
import tkinter as tk
from dataclasses import dataclass, field
from tkinter import ttk
from tkinter.constants import LEFT, RIGHT, BOTH, X, Y, W, E, N, S
from typing import Optional

from forge import __app_name__, __author__, __tagline__, __version__
from forge.hwp_session import (
    HwpInstance,
    HwpSession,
    MultipleHwpInstancesError,
    NoExistingHwpError,
    attach_or_create,
    attach_to_instance,
    init_com_for_thread,
    is_alive,
    list_hwp_instances,
)
from forge.formatter.templates import REPORT1_SPEC, ReportSpec

from .hotkeys import GlobalHotkeyManager, MOD_CONTROL, MOD_SHIFT, vk
from .hwp_picker import pick_hwp_instance
from .icon import make_app_icon
from .tabs.howto_tab import HowToTab
from .tabs.settings_tab import SettingsTab
from .tabs.markdown_tab import MarkdownTab
from .tabs.realtime_tab import RealtimeTab


# ==========================================================================
# 앱 상태 — 모든 탭이 공유
# ==========================================================================

@dataclass
class AppState:
    """탭 간 공유 상태. 한/글 세션 + 현재 spec + 사용자 선택한 인스턴스 식별자."""
    hwp: Optional[HwpSession] = None
    spec: ReportSpec = field(default_factory=lambda: REPORT1_SPEC.clone())
    # ★ 사용자가 picker 로 명시 선택한 한/글 인스턴스 moniker.
    # ROT 가 여러 한/글을 가지고 있을 때 silent first-match 로 엉뚱한 인스턴스에
    # 붙는 사고를 막기 위해 영구 저장. ensure_hwp 가 이 값을 우선 사용.
    preferred_moniker: Optional[str] = None


# ==========================================================================
# 메인 앱
# ==========================================================================

class ForgeApp:
    """Forge 메인 윈도우."""

    def __init__(self):
        self.root = tk.Tk()
        # stdlib ttk 기본 테마 — Windows 에서는 'vista' 가 가장 깔끔. 실패 시 무시.
        try:
            ttk.Style().theme_use("vista")
        except tk.TclError:
            pass
        # 버전은 노출하지 않음 — '?' About 다이얼로그에서만 확인.
        self.root.title(__app_name__)

        # 앱 아이콘 — 코드로 생성한 PhotoImage. ★ self._app_icon 속성에 보관해야
        # GC 되지 않고 윈도우 아이콘이 유지됨. iconphoto(True, ...) 로 모든
        # Toplevel (picker, about 등) 도 동일 아이콘 상속.
        self._app_icon = make_app_icon(self.root)
        try:
            self.root.iconphoto(True, self._app_icon)
        except tk.TclError:
            pass
        self.root.geometry("1200x900")
        self.root.minsize(960, 700)

        self.state = AppState()
        self._build_ui()

        # ★ 한/글은 lazy 연결 (앱 시작 시 안 띄움).
        # 사용자가 탭 ② 변환 / 탭 ③ 룰 버튼을 누르면 ensure_hwp() 가 attach 시도.
        # ─ ROT 후보가 1 개: 자동 attach (사용자가 명시 선택한 셈)
        # ─ 0 개: NoExistingHwpError → 한/글 먼저 실행 안내
        # ─ 2+ 개: MultipleHwpInstancesError → picker 다이얼로그 (silent first-match 금지)
        # status bar 의 '한/글 선택' 버튼으로 언제든 picker 열어 인스턴스 변경 가능.

        # ★ 시스템 전역 hotkey — Tk bind_all 은 Forge 포커스 시만 발화하므로
        # 한/글에서 작업 중에도 hotkey 가 작동하도록 Win32 RegisterHotKey 사용.
        self._setup_hotkeys()

        # 종료 시 정리
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)

    # ---------------------------------------------------------------- hotkeys
    def _setup_hotkeys(self) -> None:
        """탭 ① 의 룰을 Ctrl+Shift+Q/W/A/S/D/F/Z/X 시스템 전역 hotkey 로 등록."""
        rt = self.tab_realtime
        mods = MOD_CONTROL | MOD_SHIFT
        self.hotkey_mgr = GlobalHotkeyManager(self.root)
        self.hotkey_mgr.add(1, mods, vk("Q"), rt.hotkey_auto_align,             "Ctrl+Shift+Q (자동 정렬)")
        self.hotkey_mgr.add(2, mods, vk("W"), rt.hotkey_word_pull,              "Ctrl+Shift+W (어절 끌어올림)")
        self.hotkey_mgr.add(3, mods, vk("A"), rt.hotkey_font_1,                 "Ctrl+Shift+A (폰트1)")
        self.hotkey_mgr.add(4, mods, vk("S"), rt.hotkey_font_2,                 "Ctrl+Shift+S (폰트2)")
        self.hotkey_mgr.add(5, mods, vk("D"), rt.hotkey_summary_font,           "Ctrl+Shift+D (요약 폰트)")
        self.hotkey_mgr.add(6, mods, vk("F"), rt.hotkey_paragraph_size_8,       "Ctrl+Shift+F (글자크기)")
        self.hotkey_mgr.add(7, mods, vk("Z"), rt.hotkey_kerning_reset,          "Ctrl+Shift+Z (자간 0)")
        self.hotkey_mgr.add(8, mods, vk("X"), rt.hotkey_md_convert_selection,   "Ctrl+Shift+X (선택→md 변환)")

        results = self.hotkey_mgr.start()
        # 각 행의 ✓/✗ 상태 라벨 갱신 (시각 피드백)
        try:
            rt.set_initial_hk_results(results)
        except Exception:
            pass
        # 등록 결과 status bar 에 요약 — 충돌(다른 앱이 잡고 있음) 한 항목 노출
        failed = [label for label, ok in results if not ok]
        if failed:
            msg = "⚠ 단축키 등록 실패: " + ", ".join(failed) + " — 다른 앱이 이미 사용 중일 수 있음"
            self._set_status(msg)
            print(f"[hotkey] 일부 등록 실패: {failed}")

    # ---------------------------------------------------------------- UI
    def _build_ui(self) -> None:
        # 상단 status bar
        top = ttk.Frame(self.root, padding=(10, 6))
        top.pack(side="top", fill=X)

        self.status_var = tk.StringVar(
            value="한/글 미연결 — 작업 시 자동 연결됩니다"
        )
        status_label = ttk.Label(
            top, textvariable=self.status_var, padding=(8, 4),
        )
        status_label.pack(side=LEFT)

        # ? 버튼 — About 다이얼로그 (버전·작성자·태그라인). 가장 우측.
        ttk.Button(
            top, text="?", command=self._show_about, width=3,
        ).pack(side=RIGHT, padx=(6, 0))

        # 한/글 선택/변경 버튼 — 항상 picker 다이얼로그 (인스턴스 1 개면 즉시 attach).
        # 클릭으로 언제든 다른 인스턴스로 전환 가능.
        self.connect_btn = ttk.Button(
            top, text="한/글 선택", command=self._open_picker, width=14,
        )
        self.connect_btn.pack(side=RIGHT, padx=(6, 0))

        # 메인 노트북
        notebook = ttk.Notebook(self.root)
        notebook.pack(fill=BOTH, expand=True, padx=10, pady=(0, 10))

        # 탭 인스턴스화 — 각 탭은 app 참조 보유 (state + ensure_hwp 호출 등)
        self.tab_howto = HowToTab(notebook)
        self.tab_settings = SettingsTab(notebook, self.state)
        self.tab_markdown = MarkdownTab(notebook, self)
        self.tab_realtime = RealtimeTab(notebook, self)

        # 첫 사용자가 바로 안내를 볼 수 있도록 How to? 를 맨 앞에 배치.
        # 익숙해진 사용자는 탭 ① 로 이동해 작업.
        notebook.add(self.tab_howto.frame, text="ⓘ How to?")
        notebook.add(self.tab_realtime.frame, text="① 개별 작업")
        notebook.add(self.tab_settings.frame, text="② 기본정보")
        notebook.add(self.tab_markdown.frame, text="③ 마크다운 입력")
        # 기본 활성 탭은 실시간(가장 자주 쓰는 모드). How to? 는 사용자가 클릭해서 본다.
        try:
            notebook.select(self.tab_realtime.frame)
        except tk.TclError:
            pass

        # 하단 footer
        footer = ttk.Frame(self.root, padding=(10, 4))
        footer.pack(side="bottom", fill=X)
        ttk.Label(
            footer,
            text=f"{__app_name__}  •  "
                 f"활성 한/글 정형조작 + 개조식 md → hwpx 변환",
        ).pack(side=LEFT)

    # ------------------------------------------------------------ 한/글
    def ensure_hwp(self) -> HwpSession:
        """
        한/글 COM 인스턴스 보장. 워커 스레드에서 호출 가능.

        선택 정책:
          1. 살아있는 세션이 이미 있으면 그대로 반환.
          2. 사용자가 picker 로 골라둔 moniker (`preferred_moniker`) 있으면
             그 인스턴스만 attach 시도. 사라졌으면 NoExistingHwpError.
          3. 골라둔 게 없는데 ROT 후보가 0 개면 NoExistingHwpError.
          4. 골라둔 게 없는데 후보가 1 개면 silent attach.
          5. 골라둔 게 없는데 후보가 2+ 개면 MultipleHwpInstancesError 를
             instances 와 함께 raise — 워커가 잡아서 UI 스레드에 picker
             다이얼로그 요청.

        ★ silent first-match 금지. 여러 한/글이 떠 있을 때 임의 선택해서
          사용자가 의도하지 않은 곳에 편집되는 사고 방지.
        """
        if self.state.hwp is not None and is_alive(self.state.hwp.hwp):
            return self.state.hwp
        # 죽은 핸들이거나 처음 연결 — 정리 후 재시도
        self.state.hwp = None
        self._set_status("한/글 연결 중...")

        if self.state.preferred_moniker is not None:
            # 사용자가 명시 선택한 인스턴스만 시도
            session = attach_or_create(
                visible=True, allow_spawn=False,
                prefer_moniker=self.state.preferred_moniker,
            )
            self.state.hwp = session
            self._set_status(self._format_connect_status(session))
            return session

        # 미선택 — 후보 enum 후 0/1/n 분기
        instances = list_hwp_instances()
        if not instances:
            raise NoExistingHwpError(
                "떠 있는 한/글 인스턴스를 찾지 못했습니다. "
                "한/글을 먼저 직접 실행해 주세요 (빈 새 문서 또는 임의의 hwpx 파일을 연 상태)."
            )
        if len(instances) == 1:
            session = attach_to_instance(instances[0])
            self.state.hwp = session
            self.state.preferred_moniker = session.moniker_name
            self._set_status(self._format_connect_status(session))
            return session
        # 다중 후보 — picker 강제
        raise MultipleHwpInstancesError(instances)

    def _show_about(self) -> None:
        """? 버튼 — 프로젝트 정보 다이얼로그 (버전·작성자·태그라인).

        모든 표시값은 forge.__init__ 의 상수에서 단일 진실원본으로 가져옴.
        """
        win = tk.Toplevel(self.root)
        win.title(f"About {__app_name__}")
        win.transient(self.root)
        win.grab_set()
        win.resizable(False, False)

        body = ttk.Frame(win, padding=24)
        body.pack(fill=BOTH, expand=True)

        ttk.Label(body, text=__app_name__, font=("", 20, "bold")).pack(anchor="w")
        ttk.Label(
            body, text=f"v{__version__}", foreground="#888",
        ).pack(anchor="w", pady=(0, 14))
        ttk.Label(body, text=f"Author: {__author__}").pack(anchor="w")
        ttk.Label(
            body, text=f'"{__tagline__}"',
            foreground="#555", font=("", 10, "italic"),
        ).pack(anchor="w", pady=(8, 16))

        ttk.Button(body, text="확인", command=win.destroy, width=10).pack(anchor="e")

        # 부모 윈도우 중앙 정렬
        try:
            self.root.update_idletasks()
            win.update_idletasks()
            px = self.root.winfo_rootx()
            py = self.root.winfo_rooty()
            pw = self.root.winfo_width()
            ph = self.root.winfo_height()
            ww = win.winfo_reqwidth()
            wh = win.winfo_reqheight()
            x = px + (pw - ww) // 2
            y = py + (ph - wh) // 3
            win.geometry(f"+{max(x, 0)}+{max(y, 0)}")
        except Exception:
            pass

        win.bind("<Escape>", lambda _evt: win.destroy())
        win.bind("<Return>", lambda _evt: win.destroy())

    def _open_picker(self) -> None:
        """
        '한/글 선택' 버튼 핸들러 — UI 스레드에서 picker 다이얼로그.

        클릭 시점에 ROT 를 새로 enum 하고, 결과 1+개면 picker 표시.
        선택 후 즉시 attach + preferred_moniker 저장. 0 개면 안내 messagebox.
        이 함수는 UI 스레드에서 호출되므로 Tkinter 다이얼로그 직접 사용 가능.
        """
        from tkinter import messagebox
        # 메인 스레드의 COM init (각 스레드마다 한 번씩 필요. 호출 idempotent)
        try:
            init_com_for_thread()
        except Exception:
            pass

        try:
            instances = list_hwp_instances()
        except Exception as e:
            messagebox.showerror("한/글 선택 실패", f"인스턴스 목록 조회 실패:\n{e}")
            return

        if not instances:
            messagebox.showwarning(
                "한/글 미실행",
                "현재 떠 있는 한/글 인스턴스를 찾지 못했습니다.\n"
                "한/글을 먼저 실행하시고 다시 '한/글 선택' 을 눌러주세요.",
            )
            self._set_status("한/글 미연결 — 한/글을 먼저 실행해주세요")
            return

        chosen = pick_hwp_instance(
            self.root, instances,
            current_moniker=self.state.preferred_moniker,
        )
        if chosen is None:
            return  # 사용자가 취소
        self._adopt_instance(chosen)

    def _adopt_instance(self, instance: HwpInstance) -> None:
        """선택된 HwpInstance 를 정식 세션으로 승격하고 상태 업데이트."""
        session = attach_to_instance(instance)
        self.state.hwp = session
        self.state.preferred_moniker = session.moniker_name
        self._set_status(self._format_connect_status(session, instance))
        # 버튼 라벨도 '연결 중' 표시 → '한/글 변경' 으로
        try:
            self.connect_btn.config(text="한/글 변경")
        except Exception:
            pass

    def prompt_pick_from_worker(self, instances: list[HwpInstance]) -> None:
        """
        워커가 MultipleHwpInstancesError 를 잡았을 때 호출하는 helper.

        UI 스레드에서 picker 다이얼로그를 띄우도록 root.after 로 스케줄.
        워커는 이 함수 호출 후 작업을 종료하고 사용자에게 '다시 클릭' 안내.
        """
        from tkinter import messagebox

        def _show():
            chosen = pick_hwp_instance(
                self.root, instances,
                current_moniker=self.state.preferred_moniker,
            )
            if chosen is not None:
                self._adopt_instance(chosen)
                messagebox.showinfo(
                    "한/글 선택 완료",
                    f"{chosen.display_label}\n선택되었습니다. 작업 버튼을 다시 눌러주세요.",
                )
        try:
            self.root.after(0, _show)
        except Exception:
            pass

    @staticmethod
    def _format_connect_status(
        session: HwpSession, instance: Optional[HwpInstance] = None,
    ) -> str:
        """연결 결과를 사용자 친화적 status 메시지로. 파일명까지 포함."""
        # 버전 + 인덱스 + 파일명 suffix
        if session.moniker_name:
            ver_part = f"{session.version_name} #{session.instance_index}"
        else:
            ver_part = "한/글"

        # 파일명 — instance 가 전달되면 그것에서, 아니면 session.hwp.Path 에서
        file_part = ""
        try:
            if instance is not None and instance.active_file_path:
                import os
                file_part = f" — {os.path.basename(instance.active_file_path)}"
            else:
                # session.hwp 에서 직접 조회 (best-effort)
                p = ""
                try:
                    p = str(session.hwp.Path or "")
                except Exception:
                    pass
                if p:
                    import os
                    file_part = f" — {os.path.basename(p)}"
                else:
                    file_part = " — (새 문서)"
        except Exception:
            file_part = ""

        if not session.is_new:
            return f"✔ {ver_part}{file_part}"
        if session.pre_existing:
            return f"⚠ 한/글 새 인스턴스 생성 (기존 attach 불가) [{ver_part}]"
        return f"✔ 한/글 새로 띄움 [{ver_part}]"

    def _set_status(self, msg: str) -> None:
        """status bar 업데이트 (어느 스레드에서 호출해도 안전)."""
        try:
            self.root.after(0, lambda: self.status_var.set(msg))
        except Exception:
            pass

    # ------------------------------------------------------------ 종료
    def _on_close(self) -> None:
        # 시스템 전역 hotkey 해제 — 등록 채로 두면 OS 가 잡고 있음 (메모리 leak,
        # 다른 앱 실행 시 충돌 원인)
        try:
            self.hotkey_mgr.stop()
        except Exception:
            pass

        # 사용자가 작업 중인 한/글은 종료하지 않음 (보존 우선)
        # 신규 생성한 빈 한/글만 정리
        if self.state.hwp is not None and self.state.hwp.is_new:
            try:
                # 빈 신규 인스턴스만 종료. 사용자가 뭔가 작업했으면 그대로 둠
                if self.state.hwp.hwp.XHwpDocuments.Count <= 1:
                    pass  # 보수적으로: 일단 종료 안 함
            except Exception:
                pass
        self.root.destroy()

    # ------------------------------------------------------------ run
    def run(self) -> None:
        self.root.mainloop()


# ==========================================================================
# 진입점
# ==========================================================================

def main() -> int:
    app = ForgeApp()
    app.run()
    return 0


if __name__ == "__main__":
    sys.exit(main())
