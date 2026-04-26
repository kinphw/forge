"""
MetadataRenderer — 보고서명 노란 박스 + 부서·일자 stamp.

tool2 매핑:
  - 노란 박스 = `금감원페이지대제목` (한컴라이브러리.py:14245-14257)
  - stamp     = `금감원페이지` 본문 14454-14460 (인라인)

spec v1.4 (2026-04-26): 보고서명만 markdown front-matter, 작성부서·작성일은
사용자가 UI 에서 별도 입력. 본 렌더러는 그 3 값을 모두 인자로 받음.
"""
from __future__ import annotations

from datetime import date, datetime
from typing import Optional

from . import primitives as p
from .base import ElementRenderer


class MetadataRenderer(ElementRenderer):
    """보고서 헤더 (대제목 + stamp) 렌더링."""

    def render(
        self,
        보고서명: Optional[str],
        작성부서: Optional[str] = None,
        작성일: Optional[str | date] = None,
    ) -> None:
        """현재 커서 위치에 헤더 1세트 (대제목 + stamp) 삽입."""
        if 보고서명:
            self._render_title_box(보고서명)
        if 작성부서 or 작성일:
            self._render_stamp(작성부서, 작성일)

    # --------------------------------------------------------- 노란 박스 제목
    def _render_title_box(self, 제목: str) -> None:
        s = self.spec
        hwp = self.hwp

        # 1×1 표 (가로: 본문 폭 - 여백, 세로: 10.5mm)
        # tool2 `205 - 문단여백측정()` 와 동치. 문단여백측정은 좌·우 여백
        # 합 (mm) 을 반환하므로 그대로 빼면 됨.
        usable_width = 205 - (s.margins.left + s.margins.right)
        p.make_table(hwp, [usable_width], [s.title_box_height_mm])

        # 외곽 테두리 굵기
        thick = s.title_border_thickness
        p.set_table_border_thickness(hwp, thick, thick, thick, thick)

        # 노란 배경
        p.set_table_bg(hwp, *s.title_bg_rgb)

        # 글자 모양
        p.set_font(hwp, s.title_font, s.title_size_pt, bold=False)
        p.align(hwp, "center")

        # 본문
        p.insert_text(hwp, 제목)

        # 표 탈출
        p.move_right(hwp)
        p.break_para(hwp)
        p.align(hwp, "justify")

    # ------------------------------------------------------------ stamp
    def _render_stamp(
        self,
        부서: Optional[str],
        일자: Optional[str | date],
    ) -> None:
        s = self.spec
        hwp = self.hwp

        # 형식: (부서, 'YY.M.D.)
        부서_s = (부서 or "").strip()
        일자_s = self._format_date_stamp(일자)
        if 부서_s and 일자_s:
            stamp = f"({부서_s}, {일자_s})"
        elif 부서_s:
            stamp = f"({부서_s})"
        elif 일자_s:
            stamp = f"({일자_s})"
        else:
            return

        p.set_line_spacing(hwp, s.line_spacing_default)
        p.align(hwp, "right")
        p.set_font(hwp, s.date_font, s.date_size_pt, bold=False)
        p.insert_text(hwp, stamp)
        p.break_para(hwp)
        p.align(hwp, "justify")

    @staticmethod
    def _format_date_stamp(일자: Optional[str | date]) -> str:
        """YYYY-MM-DD 또는 date → 'YY.M.D. 형식 (작은따옴표 포함)."""
        if 일자 is None or 일자 == "":
            return ""
        if isinstance(일자, date) and not isinstance(일자, datetime):
            d = 일자
        elif isinstance(일자, datetime):
            d = 일자.date()
        else:
            try:
                d = datetime.strptime(str(일자), "%Y-%m-%d").date()
            except ValueError:
                return str(일자)  # 알 수 없는 형식이면 그대로
        return f"’{d.year % 100}.{d.month}.{d.day}."
