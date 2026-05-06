"""
SubsectionRenderer — 소제목 (가./나./[1]/[2]).

tool2 매핑: `금감원페이지소제목(번호, 내용)` (한컴라이브러리.py:14287-14316)

3 셀 표 (마커 셀 + 1mm 분리 셀 + 내용 셀).
"""
from __future__ import annotations

from . import primitives as p
from .base import ElementRenderer


class SubsectionRenderer(ElementRenderer):
    """라벤더 박스 마커 + 진파랑 테두리 내용 셀로 소제목 렌더링."""

    def render(self, 번호: str, 제목: str) -> None:
        """
        번호: '가' / '나' / '1' / '2' 등 (1자~몇 자 짧은 라벨)
        제목: '개요' / '진행상황' 등
        """
        s = self.spec
        hwp = self.hwp

        # 줄 시작 아니면 break (cursor 모드 안전망).
        # 위 빈 줄 prepend 는 dispatcher 가 일괄 책임 — 여기서 emit 안 함.
        if not p.is_at_line_start(hwp):
            p.break_para(hwp)
        p.align(hwp, "justify")

        # 3 셀 표: [마커폭, 1mm 분리, 내용폭]
        p.make_table(hwp, [
            s.subsection_marker_width_mm, 1.0, s.subsection_content_width_mm,
        ], [s.subsection_box_height_mm])

        # 첫 셀 = 마커 셀: 진파랑 테두리 + 라벤더 배경
        p.set_table_border_color(hwp, *s.subsection_border_rgb)
        p.set_table_bg(hwp, *s.subsection_marker_bg_rgb)
        p.set_table_border_thickness(hwp, 6, 6, 6, 6)

        p.set_font(hwp, s.subsection_font, s.subsection_marker_size_pt, bold=False)
        p.align(hwp, "center")
        p.insert_text(hwp, 번호)

        # → 가운데 1mm 셀 (분리)
        p.move_table_right(hwp, 1)
        p.set_table_border_type(hwp, top=0, bottom=0, left=1, right=1)

        # → 내용 셀
        p.move_table_right(hwp, 1)
        p.set_table_border_color(hwp, *s.subsection_border_rgb)
        p.set_table_border_thickness(hwp, 6, 6, 6, 6)
        p.set_font(hwp, s.subsection_font, s.subsection_content_size_pt, bold=False)
        p.insert_text(hwp, 제목)

        # 표 탈출
        p.move_right(hwp)
        p.break_para(hwp)
        p.align(hwp, "justify")
