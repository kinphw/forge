"""
Forge 단축키 가능한 action 카탈로그 — single source of truth.

이 파일이 (안정 id, 기본 키, 사용자 라벨, 호출 함수) 4 튜플을 묶어
한 곳에 정의. 시스템 전역 hotkey 등록 ([app.py][forge.ui.app._setup_hotkeys])
은 이 리스트를 순회하며 자동으로 만들어짐. 향후 user-settings.json 영속화
시 `id` 가 안정 키로 쓰여 `default_key` 가 사용자 override 로 덮임.

★ id 는 **영속화 안정 키 — 절대 변경 금지**. 사용자가 저장한 keymap 파일이
  그대로 깨짐.
★ ACTIONS 리스트의 순서가 hk_id (1-indexed) 가 됨. 새 action 은 끝에 추가
  하면 기존 hk_id 가 보존됨. 중간 삽입·삭제는 hk_id 가 밀리므로 피할 것.

새 action 추가 절차:
  1. 이 파일 끝에 ActionDef 추가
  2. (선택) [realtime_tab][forge.ui.tabs.realtime_tab] 에 _build_hotkey_widget
     호출 + 본체 메서드(`_run_*` 또는 `_apply_*`) 추가
  → app.py 의 _setup_hotkeys 는 안 건드림 (자동)
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, Callable

from forge.linter import fit_current_paragraph_to_one_line

if TYPE_CHECKING:
    from .tabs.realtime_tab import RealtimeTab


@dataclass(frozen=True)
class ActionDef:
    """단축키 가능한 action 1 개의 정의.

    Fields:
        id: 영속화 안정 키 — 절대 변경 X (사용자 settings.json 의 lookup 키).
        default_key: 키 1글자. 사용자 설정 부재 시 fallback.
        label: UI 표시 + RegisterHotKey 라벨.
        invoke: 호출 실행. RealtimeTab 인스턴스 (rt) 를 받음 — var_*,
            _run_*, _apply_* 등 모든 핸들러에 접근 가능.
    """
    id: str
    default_key: str
    label: str
    invoke: Callable[["RealtimeTab"], None]


# ── 등록 순서 = hk_id (1-indexed). 끝에만 추가, 중간 삭제 금지 ──────────────
ACTIONS: list[ActionDef] = [
    ActionDef(
        "auto_align", "Q", "자동 정렬",
        lambda rt: rt._run_kerning_then_indent(),
    ),
    ActionDef(
        "word_pull", "W", "어절 끌어올림",
        lambda rt: rt._run_paragraph_rule(
            "어절 끌어올림", fit_current_paragraph_to_one_line,
        ),
    ),
    ActionDef(
        "font_body", "A", "본문 폰트",
        lambda rt: rt._apply_font(rt.var_font1.get(), rt.var_size1.get()),
    ),
    ActionDef(
        "font_annotation", "S", "주석 폰트",
        lambda rt: rt._apply_font(rt.var_font2.get(), rt.var_size2.get()),
    ),
    ActionDef(
        "font_headline", "F", "헤드라인 폰트",
        lambda rt: rt._apply_font(rt.var_font3.get(), rt.var_size3.get()),
    ),
    ActionDef(
        "font_uleungdo", "G", "울릉도 폰트",
        lambda rt: rt._apply_font(rt.var_font4.get(), rt.var_size4.get()),
    ),
    ActionDef(
        "para_size_8", "D", "현재 문단 글자크기",
        lambda rt: rt._run_paragraph_size_8(),
    ),
    ActionDef(
        "kerning_reset", "Z", "자간 0",
        lambda rt: rt._run_kerning_reset(),
    ),
    ActionDef(
        "md_convert_sel", "X", "선택→md 변환",
        lambda rt: rt._run_md_convert_selection(),
    ),
]


def action_by_hk_id(hk_id: int) -> ActionDef:
    """hk_id (1-indexed) → ActionDef. 잘못된 id 면 IndexError."""
    return ACTIONS[hk_id - 1]


def hk_id_of(action_id: str) -> int:
    """action.id → hk_id (1-indexed). 못 찾으면 ValueError."""
    for i, a in enumerate(ACTIONS, start=1):
        if a.id == action_id:
            return i
    raise ValueError(f"unknown action id: {action_id!r}")
