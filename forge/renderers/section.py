"""
SectionRenderer — 중제목 (Ⅰ./Ⅱ./...).

tool2 매핑: `금감원페이지중제목(숫자, 내용)` (한컴라이브러리.py:14260-14284)
"""
from __future__ import annotations

from . import primitives as p
from .base import ElementRenderer


_ROMAN = {
    1: "Ⅰ", 2: "Ⅱ", 3: "Ⅲ", 4: "Ⅳ", 5: "Ⅴ",
    6: "Ⅵ", 7: "Ⅶ", 8: "Ⅷ", 9: "Ⅸ", 10: "Ⅹ",
    11: "Ⅺ", 12: "Ⅻ",
}


class SectionRenderer(ElementRenderer):
    """파란 밑줄 표 안에 'Ⅰ. 본문' 1줄 렌더링."""

    def render(self, 번호: int, 제목: str) -> None:
        s = self.spec
        hwp = self.hwp

        # 줄 시작 아니면 break
        if not p.is_at_line_start(hwp):
            p.break_para(hwp)
        # 위 빈 줄 (8pt)
        p.set_font(hwp, s.section_title_font, 8.0, bold=False)
        p.break_para(hwp)
        p.align(hwp, "justify")

        # 1×1 표
        # tool2 `205 - 문단여백측정()` — 문단여백측정 = 좌·우 여백 합 (mm).
        usable_width = 205 - (s.margins.left + s.margins.right)
        p.make_table(hwp, [usable_width], [s.section_box_height_mm])
        p.set_cell_margin_zero(hwp)

        # 외곽 테두리: 하단만 실선
        p.set_table_border_type(hwp, top=0, bottom=1, left=0, right=0)
        p.set_table_border_thickness(hwp, top=6, bottom=8, left=6, right=6)
        p.set_table_border_color(hwp, *s.section_underline_rgb)

        # 숫자 부분 (HY견명조 15pt Bold)
        p.set_font(hwp, s.section_number_font, s.section_number_size_pt,
                    bold=s.section_number_bold)
        p.insert_text(hwp, f"{self.to_roman(번호)}. ")

        # 내용 부분 (HY헤드라인M 16pt)
        p.char_normal(hwp)
        p.set_font(hwp, s.section_title_font, s.section_title_size_pt, bold=False)
        p.insert_text(hwp, 제목)

        # 표 탈출
        p.move_right(hwp)
        p.break_para(hwp)
        p.align(hwp, "justify")

    @staticmethod
    def to_roman(n: int) -> str:
        """1~12 → 로마자, 그 외는 그대로."""
        return _ROMAN.get(n, str(n))
