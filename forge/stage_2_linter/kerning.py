"""
어절 잘림 방지 자간조정 — tool1 `word_saver` 정확 포트.

tool1 소스 (reference/tool1/hwp_auto.py:109-170) 를 그대로 옮김.
주요 차이:
  - tool1 의 전역 `to_hwp` → 우리는 hwp 인자로 받음
  - tool1 `to_hwp.Run(...)` 호출 그대로 사용 (← `hwp.HAction.Run` 이 일부
    selection 액션에서 다르게 동작할 가능성 회피)
  - tool1 `block_len`/`block_char` 정확 포트 (InitScan 모든 키워드 인자 명시)

알고리즘 (tool1 word_saver_text):
  - 매 줄 끝의 마지막 어절을 검사
  - front = MoveLineEnd → MoveSelWordBegin 으로 거꾸로 선택한 길이
  - back  = MoveSelWordEnd 로 확장한 길이
  - front >= back : SpacingDecrease (자간 -1) — 어절 끌어당김
  - front <  back : SpacingIncrease (자간 +1) — 어절을 다음 줄로
  - 줄당 최대 15회 시도, 실패 시 그 줄 변경 모두 Undo
"""
from __future__ import annotations

from typing import Any, Callable, Optional

from ..com_helpers import set_param
from ._range import apply_per_paragraph

LogFn = Optional[Callable[[str], None]]


def _noop_log(msg: str) -> None:
    pass


def _reset_kerning_to_zero(hwp: Any) -> None:
    """
    선택 영역의 자간을 0 으로 reset (tool2 `글자간격(0)` 패턴, 한컴라이브러리.py:48).
    7 개 Spacing* item (Hangul/Latin/Hanja/Japanese/User/Symbol/Other) 일괄 0.

    SpacingDecrease 누적 효과를 깨끗이 제거 후 알고리즘 다시 시작하기 위함.
    """
    set_param(hwp, "CharShape", {
        "SpacingHangul":   0,
        "SpacingLatin":    0,
        "SpacingHanja":    0,
        "SpacingJapanese": 0,
        "SpacingUser":     0,
        "SpacingSymbol":   0,
        "SpacingOther":    0,
    })


def _block_char(hwp: Any) -> str:
    """tool1 block_char 정확 포트."""
    try:
        hwp.InitScan(
            option=None, Range=0xff,
            spara=None, spos=None, epara=None, epos=None,
        )
        result = hwp.GetText()
    except Exception:
        try: hwp.ReleaseScan()
        except Exception: pass
        return ""
    try:
        hwp.ReleaseScan()
    except Exception:
        pass
    if isinstance(result, tuple) and len(result) >= 2:
        return str(result[1] or "")
    return ""


def _block_len(hwp: Any) -> int:
    return len(_block_char(hwp))


def _adjust_one_line(hwp: Any, max_attempts: int = 15, log: LogFn = None) -> None:
    """
    tool1 word_saver_text 정확 포트.
    한 줄에 자간 ±1 단계씩 ±max_attempts 회 시도.
    """
    log = log or _noop_log
    count = 0
    while True:
        hwp.Run("MoveLineEnd")
        hwp.Run("MoveSelWordBegin")
        if count >= max_attempts:
            log(f"    [line] {max_attempts}회 초과 — Undo {count}번")
            for _ in range(count):
                hwp.Run("Undo")
            break
        front = _block_len(hwp)
        if front == 0:
            log("    [line] front=0 → 줄 끝 어절 없음, 종료")
            break
        hwp.Run("MoveSelWordEnd")
        back = _block_len(hwp)
        if not (front and back):
            log(f"    [line] front={front} back={back} → 비정상, 종료")
            hwp.Run("Cancel")
            hwp.Run("Cancel")
            break
        hwp.Run("MoveWordBegin")
        hwp.Run("MoveLineEnd")
        hwp.Run("MoveSelLineBegin")
        if front >= back:
            log(f"    [line#{count}] front={front} back={back} → SpacingDecrease")
            hwp.Run("CharShapeSpacingDecrease")
        else:
            log(f"    [line#{count}] front={front} back={back} → SpacingIncrease")
            hwp.Run("CharShapeSpacingIncrease")
        count += 1
        hwp.Run("Cancel")


def _adjust_paragraph(hwp: Any, log: LogFn = None) -> None:
    """현재 문단 1개의 모든 줄에 자간조정. 끝나면 다음 문단 시작 부근.

    절차:
      1. 문단 전체 자간 0 reset (이전 SpacingDecrease 누적 제거)
      2. tool1 word_saver 알고리즘 — 매 줄 ±1 자간 ±15회 시도
    """
    log = log or _noop_log

    # 1. 자간 reset — 문단 전체 selection 후 글자간격 0 일괄 적용
    hwp.Run("MoveParaBegin")
    hwp.Run("MoveSelParaEnd")
    try:
        _reset_kerning_to_zero(hwp)
        log("  [reset] 문단 자간 0 reset 완료")
    except Exception as e:
        log(f"  [reset] 자간 reset 실패: {e}")
    hwp.Run("Cancel")
    hwp.Run("MoveParaBegin")

    # 2. tool1 word_saver 알고리즘
    start = hwp.GetPos()
    if start is None:
        log("  [para] GetPos None — 종료")
        return
    start_id = (start[0], start[1])
    log(f"  [para] start_id={start_id}")

    max_lines = 1_000
    iters = 0
    while iters < max_lines:
        prev = hwp.GetPos()
        if prev is None:
            break
        if (prev[0], prev[1]) != start_id:
            log(f"  [para] 다음 문단 진입 ({prev[:2]}) — 종료")
            break
        log(f"  [para] line iter#{iters} pos={prev!r}")
        _adjust_one_line(hwp, log=log)
        hwp.Run("MoveLineEnd")
        hwp.Run("MoveNextChar")
        cur = hwp.GetPos()
        if cur == prev:
            log("  [para] 진행 멈춤 — 종료")
            break
        iters += 1


def _adjust_objects(hwp: Any, log: LogFn) -> None:
    """
    tool1 word_saver_object 포트 — 본문 외 영역 (표 셀 등) 의 모든 list ID
    를 순회하며 _adjust_paragraph 호출. 결론 박스/참고 박스 등 표 셀 안의
    문단 자간조정.
    """
    log = log or _noop_log
    area = 1
    obj_count = 0
    while area < 100_000:
        area += 1
        try:
            hwp.SetPos(area, 0, 0)
        except Exception:
            break
        cur = hwp.GetPos()
        if cur is None or cur[0] == 0:
            break

        log(f"  [object area={area}] cur={cur!r}")
        inner_iters = 0
        while inner_iters < 1000:
            start = hwp.GetPos()
            _adjust_paragraph(hwp, log)
            new = hwp.GetPos()
            if new[0] != 0 and new[0] >= area:
                area = new[0]
            if new == start:
                break
            inner_iters += 1
        obj_count += 1
    log(f"  [object] 처리된 area: {obj_count}")


def adjust_kerning_to_avoid_word_break(hwp: Any, log: LogFn = None) -> None:
    """
    문서 전체 + 표 셀 등 객체 영역에 어절 잘림 방지 자간조정 적용.
    배치 모드 STAGE 2 후처리에서 호출.
    """
    log = log or _noop_log

    # 1) 본문 (list=0) 순회
    log("[adjust_kerning] 본문 순회 시작")
    hwp.Run("MoveDocEnd")
    end_pos = hwp.GetPos()
    hwp.Run("MoveDocBegin")

    max_paras = 100_000
    iters = 0
    while iters < max_paras:
        prev = hwp.GetPos()
        if prev == end_pos:
            break
        _adjust_paragraph(hwp, log)
        cur = hwp.GetPos()
        if cur == prev:
            log("  [본문 STOP] 진행 멈춤")
            break
        iters += 1
    log(f"[adjust_kerning] 본문 처리 완료 ({iters} 문단)")

    # 2) 객체 영역 — 결론 박스/참고 박스 등 표 셀 안 자간조정
    log("[adjust_kerning] 객체 영역 순회 시작")
    _adjust_objects(hwp, log)
    log("[adjust_kerning] 객체 영역 처리 완료")


def adjust_kerning_current_paragraph(hwp: Any, log: LogFn = None) -> None:
    """
    selection 이 있으면 범위 내 모든 문단에, 없으면 현재 캐럿 문단 1 개에
    자간조정 적용 (각 문단마다 자간 0 reset 후 tool1 word_saver 알고리즘).
    """
    log = log or _noop_log
    log("[adjust_kerning_current_paragraph] 시작")
    apply_per_paragraph(hwp, _adjust_paragraph, log)
    log("[adjust_kerning_current_paragraph] 완료")
