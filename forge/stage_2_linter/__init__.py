"""
STAGE 2 — Linter.

활성 한/글 문서를 전체 스캔하면서 일관성·가독성 룰을 일괄 적용하는 단계.
배치 모드(STAGE 1 → 2 → 3) 의 중간 단계로 호출되며, 각 룰은 STAGE 3 에서
단독 호출도 가능하도록 단일 함수로 분절되어 있다.

룰 카탈로그:
  - align_left_indent: 본문 글머리(□/○/-/·) + 주석(*/※/†) 라인의 첫 본문
    문자 위치를 그 문단의 left indent 로 설정 (tool1 super_shift_tab 포트)
  - adjust_kerning_to_avoid_word_break: 어절 잘림 방지 자간조정 (tool1
    word_saver 포트, 줄 단위 ±1 자간 ±15회 시도)
"""
from __future__ import annotations

from .indent_align import align_current_paragraph, align_left_indent
from .kerning import (
    adjust_kerning_current_paragraph,
    adjust_kerning_to_avoid_word_break,
)
from .squeeze import fit_current_paragraph_to_one_line

__all__ = [
    # 문서 전체 — 배치 모드 후처리
    "align_left_indent",
    "adjust_kerning_to_avoid_word_break",
    # 단일 문단 — 실시간 모드 (STAGE 3) 버튼
    "align_current_paragraph",
    "adjust_kerning_current_paragraph",
    # 실시간 전용 (배치 X) — 어절 1 개 끌어올림
    "fit_current_paragraph_to_one_line",
]
