"""
BulletRenderer — 본문 글머리 (□ ○ - ·).

tool2 매핑:
  - L1·L2 = `금감원페이지` 본문 14468-14498, 14506-14538 (인라인)
  - L3·L4 = tool2 본문에는 없음. Forge 자체 정의 (templates.py).

마커 뒤 `(...)` prefix(개요/요약 등) 가 있으면 그 부분만 다른 폰트
(`spec.bullet_summary_font`, default `HY울릉도M`) 로 강조. L1~L4 모든 글머리
공통. 변환 시점에 realtime_tab var_font4 (hotkey G) 가 SSOT 로 주입되어
override 됨.
"""
from __future__ import annotations

from typing import Optional

from . import primitives as p
from .base import ElementRenderer


class BulletRenderer(ElementRenderer):
    """본문 글머리 1~4단계 렌더링."""

    def render(
        self,
        level: int,
        body: str,
        summary: Optional[str] = None,
    ) -> None:
        """
        level: 1~4 (□=1, ○=2, -=3, ·=4)
        body:  본문 텍스트 (`(...)` prefix 제외한 나머지)
        summary: 마커 뒤 `(...)` 안의 텍스트. None 이면 일반 본문만.
                 L1~L4 모든 레벨에서 동일하게 강조 폰트 적용.
        """
        if not 1 <= level <= len(self.spec.bullets):
            raise ValueError(
                f"Invalid bullet level: {level} (must be 1~{len(self.spec.bullets)})"
            )

        bs = self.spec.bullets[level - 1]
        hwp = self.hwp

        # 폰트·크기·줄간격·내어쓰기 적용
        p.set_font(hwp, bs.font, bs.size_pt, bold=bs.bold)
        p.set_line_spacing(hwp, bs.line_spacing)
        p.set_indent(hwp, bs.indent_pt)
        p.align(hwp, "justify")

        # 글머리 앞 공백 + 글리프 + 뒤 공백
        p.insert_fixed_space(hwp, bs.fixed_pre)
        p.insert_text(hwp, bs.out_glyph)
        p.insert_fixed_space(hwp, bs.fixed_post)

        # 마커 뒤 `(...)` 강조 — spec.bullet_summary_font 로. L1~L4 공통.
        # crop size 는 bullet level 의 size_pt 그대로 따라감.
        if summary:
            p.set_font(hwp, self.spec.bullet_summary_font, bs.size_pt, bold=bs.bold)
            p.insert_text(hwp, f"({summary}) ")
            # 본문은 원래 폰트로 복귀
            p.set_font(hwp, bs.font, bs.size_pt, bold=bs.bold)

        # 본문
        p.insert_text(hwp, body)
        p.break_para(hwp)
