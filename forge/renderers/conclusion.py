"""
ConclusionRenderer — 결론 화살표 박스 (=>).

tool2 매핑: `금감원페이지점선박스` (한컴라이브러리.py:14398-14417)
민트 배경 + 점선 테두리 1×1 표 안에 ⇨ + 본문.
"""
from __future__ import annotations

from . import primitives as p
from .base import ElementRenderer


class ConclusionRenderer(ElementRenderer):
    """=> 마커를 민트 점선 박스로 렌더링."""

    def render(self, body: str) -> None:
        """body: => 다음에 오는 결론 텍스트. ⇨ 글머리는 자동 prepend."""
        s = self.spec
        hwp = self.hwp

        # 줄 시작 아니면 break
        if not p.is_at_line_start(hwp):
            p.break_para(hwp)
        # 위 빈 줄 (8pt)
        p.set_font(hwp, s.conclusion_font, 8.0, bold=False)
        p.break_para(hwp)
        p.align(hwp, "right")

        # 1×1 표 (가로 199.5 - 여백, 세로 18mm)
        # tool2 `199.5 - 문단여백측정()` — 문단여백측정 = 좌·우 여백 합 (mm).
        usable_width = 199.5 - (s.margins.left + s.margins.right)
        p.make_table(hwp, [usable_width], [s.conclusion_box_height_mm])

        # 모든 외곽 점선 (BorderType=3) + 굵기 2 + 민트 배경
        border_type = 3 if s.conclusion_border_dotted else 1
        p.set_table_border_type(hwp, border_type, border_type, border_type, border_type)
        p.set_table_border_thickness(hwp, 2, 2, 2, 2)
        p.set_table_bg(hwp, *s.conclusion_bg_rgb)

        # 본문 (휴먼명조 15pt, ⇨ + 본문)
        p.set_font(hwp, s.conclusion_font, s.conclusion_size_pt, bold=False)
        p.insert_text(hwp, f"⇨ {body}")

        # 표 탈출
        p.move_right(hwp)
        p.break_para(hwp)
        p.align(hwp, "justify")
