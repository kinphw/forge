"""
탭 ③ 개별 작업 — 실시간 모드.

활성 한/글 문서에 STAGE 3 룰을 사용자가 버튼으로 골라 적용. 단일 문단
단위로 즉시 적용 + 디버그 로그 창에 단계별 진행 상황 누적 표시 — 사용자가
로그를 보고 어디서 fail 했는지 진단 가능.
"""
from __future__ import annotations

import threading
from typing import TYPE_CHECKING, Callable

import ttkbootstrap as tb
from tkinter.ttk import LabelFrame as TtkLabelFrame
from ttkbootstrap.scrolled import ScrolledText
from ttkbootstrap.constants import LEFT, X, BOTH, W

from forge.hwp_session import attach_or_create, init_com_for_thread
from forge.stage_2_linter import (
    adjust_kerning_current_paragraph,
    align_current_paragraph,
    fit_current_paragraph_to_one_line,
)

if TYPE_CHECKING:
    from ..app import ForgeApp


class RealtimeTab:
    def __init__(self, parent: tb.Window, app: "ForgeApp"):
        self.app = app
        self.state = app.state
        self.frame = tb.Frame(parent, padding=20)

        # 안내 헤더
        title = tb.Label(
            self.frame,
            text="③ 개별 작업 — 실시간 모드",
            font=("", 14, "bold"),
        )
        title.pack(anchor=W, pady=(0, 6))

        desc = tb.Label(
            self.frame,
            text="활성 한/글 문서(.hwp 또는 .hwpx)에 STAGE 3 룰을 사용자가 버튼으로 골라 즉시 적용.\n"
                 "각 버튼 클릭 시 진행 상황이 아래 로그창에 누적 출력 — 디버깅·진단용.",
            wraplength=900, justify="left",
            bootstyle="secondary",
        )
        desc.pack(anchor=W, pady=(0, 12))

        # ─── 활성 그룹 — 현재 문단 단위 적용 ───────────────────
        active_group = TtkLabelFrame(
            self.frame,
            text="🧪 테스트 (현재 커서 위치 문단에만 적용)",
            padding=10,
        )
        active_group.pack(fill=X, pady=(0, 6))

        tb.Button(
            active_group, text="들여쓰기 정렬",
            command=lambda: self._run_paragraph_rule(
                "들여쓰기 정렬", align_current_paragraph,
            ),
            bootstyle="primary",
            width=24,
        ).pack(side=LEFT, padx=(0, 6))

        tb.Button(
            active_group, text="자간조정 (어절 잘림 방지)",
            command=lambda: self._run_paragraph_rule(
                "자간조정", adjust_kerning_current_paragraph,
            ),
            bootstyle="primary",
            width=28,
        ).pack(side=LEFT, padx=(0, 6))

        tb.Button(
            active_group, text="어절 1개 끌어올림 (자간)",
            command=lambda: self._run_paragraph_rule(
                "어절 끌어올림", fit_current_paragraph_to_one_line,
            ),
            bootstyle="primary",
            width=24,
        ).pack(side=LEFT, padx=(0, 6))

        tb.Button(
            active_group, text="자간→들여쓰기 (연속)",
            command=self._run_kerning_then_indent,
            bootstyle="success",
            width=22,
        ).pack(side=LEFT, padx=(0, 6))

        tb.Button(
            active_group, text="🧹 로그 비우기",
            command=self._clear_log,
            bootstyle="secondary-outline",
            width=14,
        ).pack(side=LEFT, padx=(20, 0))

        # ─── 디버그 로그 창 ──────────────────────────────────
        log_frame = TtkLabelFrame(self.frame, text="📋 디버그 로그", padding=6)
        log_frame.pack(fill=X, pady=(6, 12))

        self.log_text = ScrolledText(
            log_frame, height=12, autohide=True, wrap="none",
            font=("Consolas", 9),
        )
        self.log_text.pack(fill=X, expand=False)

        # ─── 미구현 placeholder 그룹들 ─────────────────────────
        groups = [
            ("📐 페이지·여백", [
                "여백 표준화 (보고서 1)",
                "쪽번호 초기화",
                "쪽번호 숨기기 / 보이기",
            ]),
            ("✏️ 글자·문단", [
                "휴먼명조 본문 적용",
                "맑은 고딕 본문 적용",
                "줄간격 150% / 120%",
            ]),
            ("📊 표 정형", [
                "셀 여백 0",
                "표 테두리 단순선",
                "합계수식 삽입",
            ]),
            ("🔧 블록 편집", [
                "다중 바꾸기",
                "여백·엔터 정리",
                "숫자 → 한글화",
            ]),
        ]

        for group_label, buttons in groups:
            group = TtkLabelFrame(self.frame, text=group_label, padding=10)
            group.pack(fill=X, pady=(0, 6))
            for label in buttons:
                btn = tb.Button(
                    group, text=label,
                    command=lambda l=label: self._not_implemented(l),
                    bootstyle="secondary-outline",
                    width=28,
                )
                btn.pack(side=LEFT, padx=(0, 4))

        footer = tb.Label(
            self.frame,
            text="※ 위 활성 그룹 외에는 골격만 노출. 후속 작업에서 forge.rules.polisher 에 연결.",
            bootstyle="warning",
        )
        footer.pack(anchor=W, pady=(8, 0))

    # ------------------------------------------------------------ 로그
    def _log(self, msg: str) -> None:
        """GUI 스레드 안전 로그 추가."""
        def append():
            try:
                # ScrolledText 의 내부 Text 위젯은 .text 속성으로 접근
                inner = getattr(self.log_text, "text", self.log_text)
                inner.insert("end", msg + "\n")
                inner.see("end")
            except Exception:
                pass
        try:
            self.app.root.after(0, append)
        except Exception:
            pass

    def _clear_log(self) -> None:
        try:
            inner = getattr(self.log_text, "text", self.log_text)
            inner.delete("1.0", "end")
        except Exception:
            pass

    # ------------------------------------------------------------ 활성 핸들러
    def _run_paragraph_rule(self, label: str, fn: Callable) -> None:
        """현재 커서 위치 문단에 룰 1개 적용. 백그라운드 + 로그."""
        self.app._set_status(f"[STAGE 3] {label} 적용 중...")
        threading.Thread(
            target=self._run_async, args=(label, fn), daemon=True,
        ).start()

    def _run_async(self, label: str, fn: Callable) -> None:
        from tkinter import messagebox
        self._log("")
        self._log(f"━━━━━━ {label} 시작 ━━━━━━")
        try:
            init_com_for_thread()
            try:
                session = attach_or_create(visible=True)
                self._log(f"[hwp] attach: {session.version_name} #{session.instance_index} "
                          f"(is_new={session.is_new})")
            except Exception as e:
                self._log(f"[hwp] attach 실패: {e}")
                self.app._set_status(f"✘ {label} 실패: 한/글 attach 불가")
                self.app.root.after(0, lambda: messagebox.showerror(
                    "한/글 미연결",
                    "한/글 인스턴스에 연결할 수 없습니다.\n"
                    "한/글을 먼저 띄우거나 상단 '한/글 연결' 버튼으로 연결을 시도해 주세요.\n\n"
                    f"세부: {e}",
                ))
                return
            # fn 에 log callback 전달
            fn(session.hwp, log=self._log)
            self._log(f"━━━━━━ {label} 완료 ━━━━━━")
            self.app._set_status(f"✔ {label} 적용 완료 (현재 문단)")
        except Exception as e:
            self._log(f"[ERROR] {type(e).__name__}: {e}")
            self.app._set_status(f"✘ {label} 실패: {e}")

    # ------------------------------------------------------------ 연속 실시 (자간 → 들여쓰기)
    def _run_kerning_then_indent(self) -> None:
        """
        자간조정 → 들여쓰기 정렬 연속 실시. 사용자 검증 결과 들여쓰기 먼저
        하면 자간조정 후 본문 위치 미세 변동으로 정렬 틀어짐. 자간 확정 후
        들여쓰기 정렬해야 본문 첫 글자 위치가 정확.

        같은 문단에 두 룰 모두 적용하기 위해 시작 시점의 문단 위치를 기록해
        두고, 자간조정 종료 후(캐럿이 다음 문단으로 자동 이동) 그 위치로
        복귀해 들여쓰기 정렬 호출.
        """
        self.app._set_status("[STAGE 3] 자간 → 들여쓰기 적용 중...")
        threading.Thread(target=self._run_combined_async, daemon=True).start()

    def _run_combined_async(self) -> None:
        from tkinter import messagebox
        from forge.stage_2_linter._range import apply_per_paragraph
        from forge.stage_2_linter.indent_align import _process_paragraph
        from forge.stage_2_linter.kerning import _adjust_paragraph

        self._log("")
        self._log("━━━━━━ 자간 → 들여쓰기 (연속) 시작 ━━━━━━")
        try:
            init_com_for_thread()
            try:
                session = attach_or_create(visible=True)
                self._log(f"[hwp] attach: {session.version_name} #{session.instance_index}")
            except Exception as e:
                self._log(f"[hwp] attach 실패: {e}")
                self.app._set_status("✘ 자간→들여쓰기 실패: 한/글 attach 불가")
                self.app.root.after(0, lambda: messagebox.showerror(
                    "한/글 미연결",
                    "한/글 인스턴스에 연결할 수 없습니다.\n"
                    "한/글을 먼저 띄우거나 상단 '한/글 연결' 버튼으로 연결을 시도해 주세요.\n\n"
                    f"세부: {e}",
                ))
                return
            hwp = session.hwp

            # 한 문단에 자간 → 들여쓰기 순차 적용. apply_per_paragraph 가
            # selection 검사 후 범위 내 모든 문단에 이 fn 을 호출.
            def _combined_one_paragraph(h, log):
                # 시작 문단 위치 기록 (자간조정 후 캐럿이 다음 문단으로 이동하므로 복귀 필요)
                h.Run("MoveParaBegin")
                start_pos = h.GetPos()
                log(f"  [combined] 문단 시작 pos={start_pos!r}")

                # 1) 자간조정 (자간 0 reset → tool1 word_saver)
                log("  --- 1단계: 자간조정 ---")
                _adjust_paragraph(h, log)

                # 2) 같은 문단에 들여쓰기 정렬 — 시작 위치로 복귀
                try:
                    h.SetPos(*start_pos)
                    log(f"  [restore] SetPos → {start_pos!r}")
                except Exception as e:
                    log(f"  [restore] 복귀 실패 ({e}) — MoveParaBegin fallback")
                    h.Run("MoveParaBegin")

                log("  --- 2단계: 들여쓰기 정렬 ---")
                _process_paragraph(h, log)

            apply_per_paragraph(hwp, _combined_one_paragraph, self._log)

            self._log("━━━━━━ 자간 → 들여쓰기 (연속) 완료 ━━━━━━")
            self.app._set_status("✔ 자간 → 들여쓰기 (연속) 완료")
        except Exception as e:
            self._log(f"[ERROR] {type(e).__name__}: {e}")
            self.app._set_status(f"✘ 자간→들여쓰기 실패: {e}")

    # ------------------------------------------------------------ 미구현 핸들러
    def _not_implemented(self, name: str) -> None:
        from tkinter import messagebox
        messagebox.showinfo(
            "향후 구현",
            f"'{name}' 는 다음 단계에서 forge.rules.polisher 에 구현 예정.",
        )

    def on_hwp_ready(self) -> None:
        pass
