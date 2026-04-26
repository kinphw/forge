"""
ElementRenderer — 모든 렌더러의 추상 베이스.

각 렌더러는:
  - 한/글 COM 인스턴스 (`hwp`)와 보고서 spec (`ReportSpec`)을 보관
  - render() 메서드로 현재 커서 위치에 요소 1개를 시각 렌더링
  - 다른 렌더러를 호출하지 않음 (조합은 dispatcher 책임)
"""
from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    # 순환 import 회피 — stage_1_formatter.__init__ 가 hwpx_writer 를 import 하고
    # hwpx_writer 가 본 모듈을 import 하므로, runtime 시 templates import 안 함.
    from ..stage_1_formatter.templates import ReportSpec


class ElementRenderer:
    """모든 렌더러의 공통 베이스. hwp + spec 보관 + render() 인터페이스."""

    def __init__(self, hwp: Any, spec: "ReportSpec"):
        self.hwp = hwp
        self.spec = spec

    def render(self, *args, **kwargs) -> None:
        """현재 한/글 커서 위치에 요소 1개 시각 렌더링."""
        raise NotImplementedError(
            f"{type(self).__name__}.render() not implemented"
        )
