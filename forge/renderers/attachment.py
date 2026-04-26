"""
AttachmentRenderer — 붙임 ([붙임], [붙임 N]).

tool2 보고서1 에는 별도 붙임 헤더 메서드 없음 (NoteCalloutRenderer 가 "참고"
대신 "붙임" 으로 동작하는 변형). Forge 자체 정의:
  - 자동 페이지 break 후 시작
  - 헤더 한 줄: '[붙임 N] 제목' (HY헤드라인M 14pt Bold, 좌정렬)
  - 가로 실선 (PageNumDisplay 또는 1×1 표 1mm)
  - 본문 줄들 그대로 삽입
"""
from __future__ import annotations

from typing import Optional

from . import primitives as p
from .base import ElementRenderer


class AttachmentRenderer(ElementRenderer):
    """[붙임 N] — 새 페이지 + 헤더 + 본문."""

    def render(self, number: Optional[int], lines: list[str]) -> None:
        """
        number: [붙임 1] → 1, [붙임] → None
        lines:  본문 줄들 (첫 줄은 보통 제목, 나머지는 본문)
        """
        s = self.spec
        hwp = self.hwp

        # 페이지 break
        p.run(hwp, "PageBreak")

        # 헤더: [붙임 N] 첫줄
        title_label = f"[붙임 {number}]" if number is not None else "[붙임]"
        first_line = lines[0] if lines else ""
        header_text = f"{title_label} {first_line}".strip()

        p.align(hwp, "left")
        p.set_font(hwp, s.note_header_font, 14.0, bold=True)
        p.insert_text(hwp, header_text)
        p.break_para(hwp)

        # 헤더 아래 가로 실선 (1×1 표, 본문폭, 0.3mm 높이, 상단만 실선 — 트릭)
        # tool2 `199.5 - 문단여백측정()` — 문단여백측정 = 좌·우 여백 합 (mm).
        usable_width = 199.5 - (s.margins.left + s.margins.right)
        p.make_table(hwp, [usable_width], [0.3])
        p.set_table_border_type(hwp, top=1, bottom=0, left=0, right=0)
        p.set_table_border_thickness(hwp, top=6, bottom=6, left=6, right=6)
        p.move_right(hwp)
        p.break_para(hwp)

        # 본문 (둘째 줄부터)
        p.set_font(hwp, "휴먼명조", 12.0, bold=False)
        for line in lines[1:]:
            p.insert_text(hwp, line)
            p.break_para(hwp)
