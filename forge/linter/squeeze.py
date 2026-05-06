"""
어절 1개가 마지막 줄로 밀려난 케이스 — 자간 자동 조정으로 끌어올림.

사용자 시나리오:
  □ (단계적용) 원상회복 명령(법 §73) 우선, 특례(법 §74)는 보충적
    활용
                  ↑ "활용" 한 어절만 다음 줄로 밀림
  → 마커 뒤 본문 영역 자간 -1 씩 줄여서 한 줄에 합침

사용자 요구:
  - 마지막 줄 어절 1개일 때만 작동, 2개 이상이면 skip + 로그
  - 마커 뒤부터 자간 조정 (마커 자체는 그대로)
  - 실시간 모드 (탭 ③) 전용 — 배치 모드에서는 호출하지 않음

알고리즘:
  1. align_left_indent 와 동일하게 본문 시작 위치(body_start) 찾기
     마커 없는 문단이면 skip.
  2. 문단 줄 수 측정. 1 줄이면 이미 한 줄, skip.
  3. 마지막 줄 텍스트 추출 → 어절 수 = .split() count
     != 1 면 skip + 로그.
  4. body_start ~ ParaEnd selection → CharShapeSpacingDecrease 반복.
     매 호출 후 줄 수 재측정 — target (= initial - 1) 도달 시 stop.
     줄 수 변화 없으면 자간 한계 도달, stop.
  5. 최대 30 회 시도.

tool2 검색 결과: `블록한줄개선` 은 날짜변환 용도, 정확히 일치 X.
자간 일괄 조정 (`자간헌터` + `글자간격`) 은 어절 카운트 미고려. 자체 작성.
"""
from __future__ import annotations

from typing import Any, Callable, Optional, Tuple

from .indent_align import (
    _block_char,
    _is_body_word,
    _is_skip_marker,
)

LogFn = Optional[Callable[[str], None]]


def _noop_log(msg: str) -> None:
    pass


def _find_body_start_pos(hwp: Any, log: LogFn) -> Optional[Tuple[int, int, int]]:
    """
    현재 캐럿이 위치한 문단의 본문 시작 위치를 찾아 GetPos 결과 반환.
    빈 문단이면 None, 그 외에는 항상 위치 반환.

    align_left_indent._process_paragraph 의 "본문 워드 발견" 단계와 동일
    패턴 — FWS 빈 워드 자동 건너뜀 + 본문 워드 발견 시 MoveWordBegin 위치.

    ★ 정책: 마커 유무 관계없이 동작 — bullet/annotation/section/subsection
      마커가 있으면 그 다음 본문 워드에, 마커 없는 prose 면 첫 워드(=ParaBegin)
      에 자간 조정 영역의 시작점을 잡음.
    """
    log = log or _noop_log

    hwp.Run("MoveParaBegin")

    # 1. 첫 텍스트 워드 — 빈 문단 판정용 (실제 마커 검증은 body 검색 단계에서 처리)
    has_text = False
    for trial in range(10):
        hwp.Run("MoveSelNextWord")
        c_raw = _block_char(hwp)
        stripped = c_raw.strip()
        log(f"  [skip-search#{trial}] raw={c_raw!r} stripped={stripped!r}")
        if stripped == "":
            hwp.Run("Cancel")
            continue
        has_text = True
        log(f"  [first-text] word={stripped!r} is_marker={_is_skip_marker(stripped)}")
        hwp.Run("Cancel")
        break

    if not has_text:
        log("  [body-start] 빈 문단 — skip")
        return None

    # 2. 본문 워드 발견까지 점프 — _is_body_word 가 마커(섹션 포함) 자동 배제.
    #    마커 없는 prose 면 첫 워드가 곧 body 워드 → 한 번에 found.
    hwp.Run("MoveParaBegin")
    hwp.Run("MoveSelNextWord")
    c_raw = _block_char(hwp)
    c = c_raw[:-1] if c_raw else ""

    iters = 0
    found = False
    last_pos = None
    while iters < 30:
        if _is_body_word(c):
            found = True
            break
        hwp.Run("Cancel")
        cur_pos = hwp.GetPos()
        hwp.Run("MoveSelNextWord")
        new_pos = hwp.GetPos()
        if cur_pos == new_pos and last_pos == cur_pos:
            break
        last_pos = cur_pos
        c_raw = _block_char(hwp)
        c = c_raw[:-1] if c_raw else ""
        iters += 1

    if not found:
        log("  [body-start] 본문 워드 못 찾음")
        hwp.Run("Cancel")
        return None

    hwp.Run("MoveWordBegin")
    pos = hwp.GetPos()
    log(f"  [body-start] pos={pos!r}")
    hwp.Run("Cancel")
    return pos


def _line_of(hwp: Any, pos: Tuple[int, int, int]) -> int:
    """주어진 pos 의 화면상 줄 번호 (KeyIndicator[5])."""
    hwp.SetPos(*pos)
    try:
        return int(hwp.KeyIndicator()[5])
    except Exception:
        return -1


def _last_line_text(hwp: Any, body_start: Tuple[int, int, int]) -> str:
    """문단 마지막 줄의 본문 텍스트 (마커 영역 제외)."""
    hwp.SetPos(*body_start)
    hwp.Run("MoveParaEnd")
    hwp.Run("MoveSelLineBegin")
    text = _block_char(hwp).strip()
    hwp.Run("Cancel")
    return text


def fit_current_paragraph_to_one_line(
    hwp: Any,
    max_iters: int = 30,
    log: LogFn = None,
) -> None:
    """
    현재 캐럿 위치 문단에서 마지막 줄에 어절 1 개만 있으면 자간 -1 반복으로
    한 줄 줄여서 끌어올림. 어절 2 개 이상이면 skip.

    실시간 모드 (탭 ③) 전용. 배치 모드 후처리에서 호출 안 함.
    """
    log = log or _noop_log
    log("[fit_to_one_line] 시작")

    # 1. 본문 시작 위치 (빈 문단이면 None)
    body_start = _find_body_start_pos(hwp, log)
    if body_start is None:
        log("[fit_to_one_line] 빈 문단 — skip")
        return

    # 2. 문단 끝
    hwp.SetPos(*body_start)
    hwp.Run("MoveParaEnd")
    para_end = hwp.GetPos()

    # 3. 줄 수 측정 (body_start ~ para_end 의 라인 차이 + 1)
    line_start = _line_of(hwp, body_start)
    line_end = _line_of(hwp, para_end)
    if line_start < 0 or line_end < 0:
        log("[fit_to_one_line] 줄 수 측정 실패 — skip")
        return
    initial_lines = line_end - line_start + 1
    log(f"  body_start line={line_start} para_end line={line_end} "
        f"→ {initial_lines} 줄")

    if initial_lines <= 1:
        log("[fit_to_one_line] 이미 한 줄 — skip")
        return

    # 4. 마지막 줄 어절 수 검사
    last_text = _last_line_text(hwp, body_start)
    word_count = len(last_text.split()) if last_text else 0
    log(f"  마지막 줄 텍스트={last_text!r} 어절수={word_count}")

    if word_count != 1:
        log(f"[fit_to_one_line] 마지막 줄 어절 {word_count} 개 (1 개 초과 또는 0) — skip")
        return

    # 5. SpacingDecrease 반복 — 한 줄 줄어들 때까지.
    #    SpacingDecrease 1 번 = 자간 -1% 정도라 한 번으로는 보통 line shift
    #    안 일어남. 누적 효과 필요. 연속 NO_CHANGE_LIMIT 회 변화 없을 때만
    #    "자간 한계" 로 판정 (tool1 word_saver 의 ±15회 시도와 비슷한 정신).
    target = initial_lines - 1
    cur = initial_lines
    no_change_count = 0
    NO_CHANGE_LIMIT = 8
    log(f"  target={target} 줄, max_iters={max_iters}, no-change limit={NO_CHANGE_LIMIT}")

    for i in range(max_iters):
        # body_start ~ para_end selection
        hwp.SetPos(*body_start)
        hwp.Run("MoveSelParaEnd")
        hwp.Run("CharShapeSpacingDecrease")
        hwp.Run("Cancel")

        # 줄 수 재측정
        new_line_start = _line_of(hwp, body_start)
        new_line_end = _line_of(hwp, para_end)
        if new_line_start < 0 or new_line_end < 0:
            log(f"  iter#{i+1}: 줄 수 측정 실패 — stop")
            break
        new_lines = new_line_end - new_line_start + 1

        if new_lines <= target:
            log(f"  iter#{i+1}: lines={new_lines} ✔ 도달")
            log(f"[fit_to_one_line] ✔ {new_lines} 줄 도달 ({i+1} 회 SpacingDecrease)")
            return

        if new_lines >= cur:
            no_change_count += 1
            log(f"  iter#{i+1}: lines={new_lines} (no-change {no_change_count}/{NO_CHANGE_LIMIT})")
            if no_change_count >= NO_CHANGE_LIMIT:
                log(f"[fit_to_one_line] ⚠ 연속 {NO_CHANGE_LIMIT} 회 변화 없음 — 자간 한계, "
                    f"현재 {cur} 줄 ({i+1} 회 SpacingDecrease 적용 후)")
                return
        else:
            log(f"  iter#{i+1}: lines={new_lines} (감소 {cur}→{new_lines})")
            cur = new_lines
            no_change_count = 0

    log(f"[fit_to_one_line] ⚠ {max_iters} 회 시도, 최종 {cur} 줄 — 한계")
