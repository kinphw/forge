"""
Sentinel-Forge 메인 GUI.

실행 시:
  1. ttkbootstrap 테마로 독립 윈도우 생성
  2. 한/글 COM 인스턴스 자동 attach (또는 신규 생성)
  3. 3-탭 노트북 (기본정보 / 마크다운 입력 / 개별 작업)
  4. 상단에 한/글 연결 상태 표시

진입: `python -m ui.app` 또는 `python run.py`
"""
from __future__ import annotations

import sys
import threading
import tkinter as tk
from dataclasses import dataclass, field
from typing import Optional

import ttkbootstrap as tb
from ttkbootstrap.constants import LEFT, RIGHT, BOTH, X, Y, W, E, N, S

from forge import __version__
from forge.hwp_session import HwpSession, attach_or_create
from forge.stage_1_formatter.templates import REPORT1_SPEC, ReportSpec

from .tabs.settings_tab import SettingsTab
from .tabs.markdown_tab import MarkdownTab
from .tabs.realtime_tab import RealtimeTab


# ==========================================================================
# 앱 상태 — 모든 탭이 공유
# ==========================================================================

@dataclass
class AppState:
    """탭 간 공유 상태. 한/글 세션 + 현재 spec."""
    hwp: Optional[HwpSession] = None
    spec: ReportSpec = field(default_factory=lambda: REPORT1_SPEC.clone())


# ==========================================================================
# 메인 앱
# ==========================================================================

class ForgeApp:
    """Sentinel-Forge 메인 윈도우."""

    def __init__(self, theme: str = "cosmo"):
        self.root = tb.Window(themename=theme)
        self.root.title(f"Sentinel-Forge v{__version__}")
        self.root.geometry("1200x900")
        self.root.minsize(960, 700)

        self.state = AppState()
        self._build_ui()

        # ★ 한/글은 lazy 연결 (앱 시작 시 안 띄움).
        # 사용자가 탭 ② 변환 / 탭 ③ 룰 버튼을 누르는 시점에 ensure_hwp() 가 자동 attach.
        # 사용자가 미리 연결 확인하고 싶거나 한/글이 죽었을 때 status bar 의
        # "한/글 연결" 버튼으로 수동 연결도 가능.

        # 종료 시 정리
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)

    # ---------------------------------------------------------------- UI
    def _build_ui(self) -> None:
        # 상단 status bar
        top = tb.Frame(self.root, padding=(10, 6))
        top.pack(side="top", fill=X)

        self.status_var = tk.StringVar(
            value="한/글 미연결 — 작업 시 자동 연결됩니다"
        )
        status_label = tb.Label(
            top, textvariable=self.status_var,
            bootstyle="inverse-secondary", padding=(8, 4),
        )
        status_label.pack(side=LEFT)

        # 수동 연결 버튼 — 보통 누를 필요 없음. 미리 확인 / 강제 재연결 용도.
        connect_btn = tb.Button(
            top, text="한/글 연결", command=self._reconnect_hwp,
            bootstyle="secondary-outline", width=12,
        )
        connect_btn.pack(side=RIGHT, padx=(6, 0))

        # 메인 노트북
        notebook = tb.Notebook(self.root)
        notebook.pack(fill=BOTH, expand=True, padx=10, pady=(0, 10))

        # 탭 인스턴스화 — 각 탭은 app 참조 보유 (state + ensure_hwp 호출 등)
        self.tab_settings = SettingsTab(notebook, self.state)
        self.tab_markdown = MarkdownTab(notebook, self)
        self.tab_realtime = RealtimeTab(notebook, self)

        notebook.add(self.tab_settings.frame, text="① 기본정보")
        notebook.add(self.tab_markdown.frame, text="② 마크다운 입력")
        notebook.add(self.tab_realtime.frame, text="③ 개별 작업")

        # 하단 footer
        footer = tb.Frame(self.root, padding=(10, 4))
        footer.pack(side="bottom", fill=X)
        tb.Label(
            footer,
            text=f"Sentinel-Forge v{__version__}  •  "
                 f"개조식 md → hwpx 변환 + 활성 한/글 정형조작",
            bootstyle="secondary",
        ).pack(side=LEFT)

    # ------------------------------------------------------------ 한/글
    def ensure_hwp(self) -> HwpSession:
        """
        한/글 COM 인스턴스 보장. 이미 연결돼 있으면 그대로 반환,
        없으면 attach (한/글 자동 spawn).

        ★ 탭의 백그라운드 worker 가 호출하는 메서드.
        호출 스레드는 미리 init_com_for_thread() 한 상태여야 함.
        """
        if self.state.hwp is not None:
            return self.state.hwp
        self._set_status("한/글 연결 중...")
        session = attach_or_create(visible=True)
        self.state.hwp = session
        kind = "신규 생성" if session.is_new else "기존 attach"
        self._set_status(f"✔ 한/글 연결됨 ({kind})")
        return session

    def _reconnect_hwp(self) -> None:
        """수동 (재)연결 버튼. 평소엔 누를 필요 없음."""
        self._set_status("한/글 연결 중...")
        self.state.hwp = None
        threading.Thread(target=self._attach_hwp_async, daemon=True).start()

    def _attach_hwp_async(self) -> None:
        """수동 연결 버튼이 누른 백그라운드 attach."""
        try:
            self.ensure_hwp()
        except Exception as e:
            err = f"✘ 한/글 연결 실패: {e}"
            self.root.after(0, lambda: self._set_status(err))

    def _set_status(self, msg: str) -> None:
        """status bar 업데이트 (어느 스레드에서 호출해도 안전)."""
        try:
            self.root.after(0, lambda: self.status_var.set(msg))
        except Exception:
            pass

    # ------------------------------------------------------------ 종료
    def _on_close(self) -> None:
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
