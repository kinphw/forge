"""
AnnotationRenderer — 주석 (* ※ †).

3 종 마커 모두 spec.annotation 단일 spec 적용. 출력 글리프는 입력 마커 그대로 보존.
"""
from __future__ import annotations

from . import primitives as p
from .base import ElementRenderer


class AnnotationRenderer(ElementRenderer):
    """*, **, ***, ※, † 모든 주석을 동일 spec(맑은 고딕 12pt 등)으로 렌더링."""

    def render(self, marker: str, body: str) -> None:
        """
        marker: '*' / '**' / '***' / '※' / '†'  — 그대로 출력
        body: 주석 본문
        """
        a = self.spec.annotation
        hwp = self.hwp

        p.set_font(hwp, a.font, a.size_pt, bold=a.bold)
        p.set_line_spacing(hwp, a.line_spacing)
        p.set_indent(hwp, a.indent_pt)
        p.align(hwp, "justify")

        p.insert_fixed_space(hwp, a.fixed_pre)
        p.insert_text(hwp, marker)
        p.insert_fixed_space(hwp, a.fixed_post)
        p.insert_text(hwp, body)
        p.break_para(hwp)
