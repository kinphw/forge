"""
NoteCalloutRenderer — [참고] 점선 박스.

tool2 매핑: `def 금감업무정보점선박스` (한컴라이브러리.py:15755-15776)
자료 탭의 "업무정보 옆 ※(참고)" 버튼 형식.

구조:
  - 1×1 표, 가로 = 198 - 좌우여백, 세로 12mm
  - 점선 테두리 (BorderType=3) + 굵기 2
  - 배경색 없음 (투명)
  - 폰트: 맑은 고딕 13pt
  - 본문 한 줄:
      "※ "      (plain)
      "(참고)"  (Bold)
      " 본문"   (plain)
  - 우정렬 (ParagraphShapeAlignRight)
"""
from __future__ import annotations

from . import primitives as p
from .base import ElementRenderer


class NoteCalloutRenderer(ElementRenderer):
    """[참고] 점선 박스 — tool2 금감업무정보점선박스 형식."""

    def render(self, lines: list[str]) -> None:
        """
        lines: [참고] 다음에 오는 본문 줄들. 첫 줄이 박스 안 한 줄에 들어가고,
               추가 줄이 있으면 BreakPara 후 같은 셀 안에 누적.
        """
        s = self.spec
        hwp = self.hwp

        # 위치 정리 — 줄 시작 아니면 break (cursor 모드 안전망).
        # 위 빈 줄 prepend 는 dispatcher 가 일괄 책임 — 여기서 emit 안 함.
        if not p.is_at_line_start(hwp):
            p.break_para(hwp)
        p.align(hwp, "right")  # tool2: ParagraphShapeAlignRight

        # 1×1 점선 표
        usable_width = 198 - (s.margins.left + s.margins.right)
        p.make_table(hwp, [usable_width], [12.0])

        # 점선 테두리 (BorderType=3) 모든 면 + 굵기 2
        p.set_table_border_type(hwp, 3, 3, 3, 3)
        p.set_table_border_thickness(hwp, 2, 2, 2, 2)

        # 본문 — 맑은 고딕 13pt
        p.set_font(hwp, "맑은 고딕", 13.0, bold=False)
        p.set_text_color(hwp, 0, 0, 0)
        p.align(hwp, "justify")

        # "※ " (plain) + "(참고)" (Bold) + " 본문" (plain)
        p.insert_text(hwp, "※ ")
        p.char_bold_on(hwp)
        p.insert_text(hwp, "(참고)")
        p.char_bold_on(hwp)  # toggle off

        if lines:
            # 첫 줄 — 앞 공백 1 + 본문
            p.insert_text(hwp, " " + lines[0])
            for extra in lines[1:]:
                p.break_para(hwp)
                p.insert_text(hwp, extra)

        # 표 탈출
        p.move_right(hwp)
        p.break_para(hwp)
        p.align(hwp, "justify")
