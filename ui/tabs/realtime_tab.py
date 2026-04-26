"""
탭 ③ 개별 작업 — 실시간 모드 (placeholder).

향후 구현:
  활성 한/글 문서에 STAGE 3 룰을 사용자가 버튼으로 골라 적용.
  tool2 패널의 100+ 단축 버튼 중 Forge 가 발췌·재구현한 룰 모음.

이번 세션에서는 UI 골격만 — 실제 룰 구현은 후속 작업.
"""
from __future__ import annotations

from typing import TYPE_CHECKING

import ttkbootstrap as tb
from tkinter.ttk import LabelFrame as TtkLabelFrame
from ttkbootstrap.constants import LEFT, X, BOTH, W, CENTER

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
                 "tool2 패널의 100+ 단축 기능 중 Forge 가 발췌·재구현한 룰 모음이 들어갈 영역.",
            wraplength=900, justify="left",
            bootstyle="secondary",
        )
        desc.pack(anchor=W, pady=(0, 16))

        # 카테고리별 버튼 placeholder (실제 룰은 차후 연결)
        groups = [
            ("📐 페이지·여백", [
                "여백 표준화 (보고서 1)",
                "쪽번호 초기화",
                "쪽번호 숨기기 / 보이기",
            ]),
            ("✏️ 글자·문단", [
                "자간 조정 (어절 줄바꿈 방지)",
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
            group.pack(fill=X, pady=(0, 8))
            for label in buttons:
                btn = tb.Button(
                    group, text=label,
                    command=lambda l=label: self._not_implemented(l),
                    bootstyle="secondary-outline",
                    width=28,
                )
                btn.pack(side=LEFT, padx=(0, 4))

        # footer 안내
        footer = tb.Label(
            self.frame,
            text="※ 이번 세션에서는 UI 골격만 노출. 실제 룰 동작은 후속 작업에서 연결됩니다.",
            bootstyle="warning",
        )
        footer.pack(anchor=W, pady=(16, 0))

    def _not_implemented(self, name: str) -> None:
        from tkinter import messagebox
        messagebox.showinfo(
            "향후 구현",
            f"'{name}' 는 다음 단계에서 forge.rules.polisher 에 구현 예정.\n\n"
            "현재 탭 ③ 은 골격만 작성된 상태입니다."
        )

    def on_hwp_ready(self) -> None:
        pass  # 향후: HWP 연결 시 버튼 활성화
