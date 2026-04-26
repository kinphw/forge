"""
NoteCalloutRenderer — [참고] 박스.

tool2 매핑: `금감원페이지참고` (한컴라이브러리.py:14420-14446)
3 셀 표 (참고 헤더 셀 + 1mm 분리 + 본문 셀).
헤더 셀: 진파랑 배경 + 흰 글씨 'HY헤드라인M 15pt Bold'.
"""
from __future__ import annotations

from . import primitives as p
from .base import ElementRenderer


class NoteCalloutRenderer(ElementRenderer):
    """[참고] 박스 — 진파랑 헤더 + 본문 셀."""

    def render(self, lines: list[str]) -> None:
        """
        lines: [참고] 다음에 오는 본문 줄들. 비어있는 줄은 caller 가 미리 제거.
        """
        s = self.spec
        hwp = self.hwp

        # 줄 시작 아니면 break + 위 빈 줄
        if not p.is_at_line_start(hwp):
            p.break_para(hwp)
        p.set_font(hwp, s.note_header_font, 8.0, bold=False)
        p.break_para(hwp)
        p.align(hwp, "justify")

        # 3 셀 표 [헤더폭 17.6, 1mm 분리, 본문폭]
        # tool2 `182 - 문단여백측정()` — 문단여백측정 = 좌·우 여백 합 (mm).
        usable_width = 182 - (s.margins.left + s.margins.right)
        p.make_table(hwp, [
            s.note_header_width_mm, 1.0, usable_width,
        ], [s.note_box_height_mm])

        # 헤더 셀: 셀 여백 0, 진파랑 배경, 흰 글씨, 가운데 정렬, '참고'
        p.set_cell_margin_zero(hwp)
        p.set_table_bg(hwp, *s.note_header_bg_rgb)
        p.char_bold_on(hwp)
        p.set_font(hwp, s.note_header_font, s.note_header_size_pt, bold=True)
        p.set_text_color(hwp, *s.note_header_text_rgb)
        p.align(hwp, "center")
        p.insert_text(hwp, "참고")

        # → 가운데 1mm 분리 셀 (좌·우만 실선)
        p.move_table_right(hwp, 1)
        p.set_table_border_type(hwp, top=0, bottom=0, left=1, right=1)

        # → 본문 셀
        p.move_table_right(hwp, 1)
        p.char_normal(hwp)
        p.set_text_color(hwp, 0, 0, 0)  # 검정으로 복귀
        p.set_font(hwp, s.note_header_font, s.note_header_size_pt, bold=False)
        p.align(hwp, "justify")
        p.insert_fixed_space(hwp, 2)

        # 첫 줄
        if lines:
            p.insert_text(hwp, lines[0])
            # 추가 줄 — BreakPara 후 들여쓰기 + 텍스트
            for extra in lines[1:]:
                p.break_para(hwp)
                p.insert_fixed_space(hwp, 2)
                p.insert_text(hwp, extra)

        # 표 탈출
        p.move_right(hwp)
        p.break_para(hwp)
        p.align(hwp, "justify")
