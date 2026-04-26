"""
AttachmentRenderer — 붙임 ([붙임], [붙임 N]).

사용자 결정 (2026-04-27): "기존의 (개선 전) 참고 형식과 같이" 렌더링.
즉 NoteCalloutRenderer 의 이전 형식(진파랑 라벨 박스 + 본문 셀) 을 [붙임]에
적용. 차이점:
  - 라벨 텍스트: "참고" 가 아닌 "붙임" 또는 "붙임 N"
  - 시작 시 페이지 break
  - 헤더 폭 약간 넓힘 (붙임 N 글자수 더 김)
  - 헤더 색은 진파랑(0,0,255), 본문 폰트 13pt (이전 우리 참고 형식 그대로)

[참고] vs [붙임 N] 시각 차이:
  [참고]   → tool2 자료탭 형식 (진남색 박스 + 본문 20pt 강조)
  [붙임 N] → 이전 형식 (진파랑 박스 + 본문 13pt + 페이지 break)
"""
from __future__ import annotations

from typing import Optional

from . import primitives as p
from .base import ElementRenderer


class AttachmentRenderer(ElementRenderer):
    """[붙임 N] — 새 페이지 + 라벨 박스 + 본문 셀."""

    def render(self, number: Optional[int], lines: list[str]) -> None:
        """
        number: [붙임 1] → 1, [붙임] → None
        lines:  본문 줄들 (첫 줄은 보통 제목, 나머지는 본문)
        """
        s = self.spec
        hwp = self.hwp

        # 페이지 break — [붙임]은 항상 새 페이지에서 시작
        p.run(hwp, "PageBreak")

        # 라벨 텍스트
        label = f"붙임 {number}" if number is not None else "붙임"

        # 줄 시작 보장 + 위 여유
        if not p.is_at_line_start(hwp):
            p.break_para(hwp)
        p.set_font(hwp, s.note_header_font, 8.0, bold=False)
        p.break_para(hwp)
        p.align(hwp, "justify")

        # 3 셀 표 [라벨폭, 1mm 분리, 본문폭] — 이전 NoteCallout 패턴
        usable_width = 182 - (s.margins.left + s.margins.right)
        p.make_table(hwp, [
            s.attach_header_width_mm, 1.0, usable_width,
        ], [s.note_box_height_mm])

        # 라벨 셀: 진파랑 배경 + 흰 글씨 + Bold + 가운데 정렬
        p.set_cell_margin_zero(hwp)
        p.set_table_bg(hwp, *s.attach_header_bg_rgb)
        p.char_bold_on(hwp)
        p.set_font(hwp, s.note_header_font, s.attach_header_size_pt, bold=True)
        p.set_text_color(hwp, *s.note_header_text_rgb)
        p.align(hwp, "center")
        p.insert_text(hwp, label)

        # → 가운데 1mm 분리 셀
        p.move_table_right(hwp, 1)
        p.set_table_border_type(hwp, top=0, bottom=0, left=1, right=1)

        # → 본문 셀
        p.move_table_right(hwp, 1)
        p.char_normal(hwp)
        p.set_text_color(hwp, 0, 0, 0)
        p.set_font(hwp, s.note_header_font, s.attach_header_size_pt, bold=False)
        p.align(hwp, "justify")
        p.insert_fixed_space(hwp, 2)

        # 본문 — 첫 줄(제목)이 헤더 셀 옆에. 이후 줄은 BreakPara
        if lines:
            p.insert_text(hwp, lines[0])
            for extra in lines[1:]:
                p.break_para(hwp)
                p.insert_fixed_space(hwp, 2)
                p.insert_text(hwp, extra)

        # 표 탈출
        p.move_right(hwp)
        p.break_para(hwp)
        p.align(hwp, "justify")

        # 붙임 본문 (별지 추가 텍스트가 있으면 표 아래에)
        # → lines 첫 줄이 제목이고 나머지가 본문이라면 위에서 셀 안에 넣음.
        #   별지에 표·이미지 등 추가는 후속.
