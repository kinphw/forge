"""
HWP COM API 호출 헬퍼.

tool2의 411개 wrapper 메서드를 그대로 답습하지 않고, 5단계 패턴
(CreateAction → CreateSet → GetDefault → SetItem → Execute) 만 1줄
함수로 묶는다. 룰 코드는 직접 COM API명을 사용 — hwp-api-mcp /
tool2-spec-mcp 검색 결과를 그대로 옮길 수 있어 self-documenting.
"""
from __future__ import annotations

from typing import Any


def set_param(hwp: Any, action: str, items: dict[str, Any]) -> None:
    """
    HWP COM 5단계 패턴을 1줄 호출로.

    예 (tool2의 자간헌터와 등가):
        set_param(hwp, "ParagraphShape", {"BreakNonLatinWord": 0})

    예 (tool2의 줄간격 등가):
        set_param(hwp, "ParagraphShape", {
            "LineSpacingType": 0,
            "LineSpacing": 150,
        })
    """
    act = hwp.CreateAction(action)
    s = act.CreateSet()
    act.GetDefault(s)
    for k, v in items.items():
        s.SetItem(k, v)
    act.Execute(s)


def insert_text(hwp: Any, text: str) -> None:
    """현재 위치에 텍스트 삽입 (tool2 '문장' 메서드 등가)."""
    set_param(hwp, "InsertText", {"Text": text})


def run(hwp: Any, action: str) -> None:
    """매개변수 없는 단순 액션 실행 (BreakPara, MoveRight, Cancel 등)."""
    hwp.HAction.Run(action)


def mm_to_hwp(hwp: Any, mm: float) -> int:
    """mm → HWP 단위 변환 (HWP 내부 단위 = 1/7200 inch)."""
    return hwp.MiliToHwpUnit(mm)


def pt_to_hwp(hwp: Any, pt: float) -> int:
    """
    pt → HWP 단위 변환.

    주의: tool2 코드는 모든 pt 값에 *2 를 적용한 후 PointToHwpUnit 호출.
    재현성을 위해 같은 관례 유지 (왜 *2 인지는 코드 주석에 미상).
    """
    return hwp.PointToHwpUnit(pt * 2)


def rgb(hwp: Any, r: int, g: int, b: int) -> int:
    """RGB 색상 → HWP COM RGBColor."""
    return hwp.RGBColor(r, g, b)
