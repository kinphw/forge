"""
탭 ⓪ How to? — 정적 안내. 코드 분기 없음.
"""
from __future__ import annotations

import tkinter as tk
from tkinter import ttk
from tkinter.constants import BOTH, W
from tkinter.ttk import LabelFrame as TtkLabelFrame

from ..scrolled import ScrolledFrame


_TABS = [
    ("① 개별 작업",  "한/글 활성 문서에 룰 1 개씩 적용 (단축키 또는 버튼)"),
    ("② 양식삽입",  "보고서 양식 8 종을 커서 위치에 1-클릭 삽입"),
    ("③ 마크다운",  "개조식 markdown → 새 .hwpx 파일로 변환 (배치)"),
]

# Forge 는 표준 markdown `#`/`##` 헤더를 쓰지 않음 — 개조식 글머리 + callout.
_MD = [
    ("보고서명: ...",      "대제목 — YAML front-matter (`---` 사이). `#` 헤더 아님"),
    ("1.  2.  3. ...",     "섹션 헤더"),
    ("가.  나.  다. ...",  "소제목"),
    ("□ / ○ / - / ·",      "본문 글머리 4 단계 (왼쪽이 상위)"),
    ("□ (요약) ...",       "요약 강조 — HY울릉도M 폰트"),
    ("* / ** / ***",       "참조 주석 (별 개수로 단계)"),
    ("※ / †",              "일반 주석"),
    ("=> ...",             "결론 박스"),
    ("[참고]",             "참고 callout (다음 빈 줄까지 본문)"),
    ("[붙임] / [붙임 N]",  "붙임 — 자동 페이지 break"),
    ("__강조__",           "인라인 Bold"),
]


def _two_col(parent: tk.Misc, items: list[tuple[str, str]],
             key_width: int = 22, key_font: tuple = ("", 10, "bold")) -> None:
    """key | desc 2 컬럼 행 묶음."""
    for key, desc in items:
        row = ttk.Frame(parent)
        row.pack(fill="x", pady=1)
        ttk.Label(row, text=key, width=key_width, anchor=W,
                  font=key_font).pack(side="left")
        ttk.Label(row, text=desc, justify="left",
                  wraplength=720).pack(side="left", anchor=W)


class HowToTab:
    def __init__(self, parent: tk.Misc):
        self.frame = ttk.Frame(parent)
        scrolled = ScrolledFrame(self.frame, padding=16)
        scrolled.pack(fill=BOTH, expand=True)
        c = scrolled.interior

        ttk.Label(c, text="How to?", font=("", 16, "bold")).pack(anchor=W, pady=(0, 6))
        ttk.Label(
            c, text="한/글을 먼저 실행해 두면 Forge 가 자동 attach 합니다.",
            foreground="#555",
        ).pack(anchor=W, pady=(0, 12))

        tabs = TtkLabelFrame(c, text="3 탭 구성", padding=10)
        tabs.pack(fill="x", pady=(0, 10))
        _two_col(tabs, _TABS)

        md = TtkLabelFrame(
            c, text="탭 ③ 마크다운 문법 — 개조식 (표준 `#` 헤더 미사용)",
            padding=10,
        )
        md.pack(fill="x", pady=(0, 10))
        _two_col(md, _MD, key_font=("Consolas", 10))

        ttk.Label(
            c, text="자세한 사용법: 우상단 ? 버튼 / README / spec/",
            foreground="#666",
        ).pack(anchor=W, pady=(4, 0))
