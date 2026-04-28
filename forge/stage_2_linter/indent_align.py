r"""
자동 들여쓰기 정렬 — tool1 `super_shift_tab_text` 정확 포트.

tool1 소스 (reference/tool1/hwp_auto.py:172-237) 와의 차이:
  - tool1 은 전역 `to_hwp` 사용 → 우리는 hwp 인자로 받음
  - tool1 은 모든 marker 라인에 적용 → 우리는 사용자 정책상 bullet
    (□/○/-/·) + annotation (*/**/.../※/†) 만 적용 (첫 워드 검증)
  - tool1 의 `to_hwp.Run(...)` 호출 방식 그대로 사용 (← `hwp.HAction.Run`
    이 일부 액션에서 다르게 동작할 가능성 회피)
  - tool1 의 `block_char` (InitScan 모든 키워드 인자 명시) 그대로

알고리즘 (tool1 super_shift_tab_text):
  1. ParaBegin / ParaEnd 줄 번호 비교 — 같으면 한 줄, skip
  2. ParaBegin → MoveSelNextWord → block_char → c[:-1]
  3. (Forge 추가) 첫 워드가 bullet/annotation marker 인지 검증.
     아니면 skip.
  4. while: 본문 워드 (alpha 있고 '.' 없음) 발견까지 워드 점프
  5. MoveWordBegin → ParagraphShapeIndentAtCaret
  6. MoveNextParaBegin
"""
from __future__ import annotations

from typing import Any, Callable, Optional

from ._range import apply_per_paragraph

LogFn = Optional[Callable[[str], None]]


def _noop_log(msg: str) -> None:
    pass


# bullet markers — 마크다운 입력 글자 + BulletRenderer 출력 글자 모두 포함.
# 한/글 본문에는 out_glyph (templates.py 의 BulletStyle.out_glyph) 가 박힘:
#   L1 □ (U+25A1)         — md/out 동일
#   L2 ○ (U+25CB) → ◦ (U+25E6)  — 입력은 CIRCLE, 출력은 WHITE BULLET
#   L3 - (U+002D)         — md/out 동일
#   L4 · (U+00B7)         — md/out 동일
# 사용자 손글 md 또는 다른 출력 변형도 흡수해 양쪽 다 매칭.
_BULLET_MARKERS = frozenset((
    "□",   # U+25A1
    "○",   # U+25CB (markdown 입력)
    "◦",   # U+25E6 (BulletRenderer L2 출력)
    "-",   # U+002D
    "·",   # U+00B7
    "⇨",   # U+21E8 — ConclusionRenderer 결론 박스 (=>)
))
_ANNOTATION_FIXED = frozenset(("※", "†"))


def _block_char(hwp: Any) -> str:
    """
    tool1 block_char 정확 포트 — 선택 영역의 텍스트.
    InitScan 모든 키워드 인자 명시 (option=None, Range=0xff, spara=None,
    spos=None, epara=None, epos=None).
    """
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


def _is_bullet_or_annotation_marker(word: str) -> bool:
    """워드가 bullet/annotation marker 인지."""
    if not word:
        return False
    if word in _BULLET_MARKERS:
        return True
    if word in _ANNOTATION_FIXED:
        return True
    if all(ch == "*" for ch in word):
        return True
    return False


def _is_body_word(c: str) -> bool:
    """
    본문 워드 판정 — 한 글자라도 alphabetic (한글 자모/음절·라틴 등) 이면 True.

    원래 tool1 은 "alpha 포함 + '.' 없음" 이었으나 그 `.` 배제 로직이 너무 공격적.
    한국어 prose 의 `반갑니다.`, `'26.4.19.자로`, `Co.` 등 자연스러운 끝점·중간점이
    들어간 첫 본문 워드를 skip 하게 되어, 다음 워드 (예: `이`/`것은`) 에 indent 가
    설정 → 두번째 줄부터 본문 첫 글자보다 한참 오른쪽으로 정렬되는 버그.

    원래 의도였던 "번호 토큰 (`1.`, `(1)`, `①`) skip" 은 alpha 가 없으므로 본 함수가
    자연스럽게 False 를 반환 — `.` 특별 처리 없어도 동작. 반대로 alpha 가 있으면
    번호가 아니라 본문이므로 body 로 인정.
    """
    return any(ch.isalpha() for ch in c)


def _process_paragraph(hwp: Any, log: LogFn = None) -> None:
    """
    tool1 super_shift_tab_text 정확 포트 + Forge marker 검증.

    ★ tool1 과 마찬가지로 `hwp.Run(...)` 사용 (HAction.Run 아님).
    """
    log = log or _noop_log

    # tool1: hwp.Run("MoveParaBegin") / hwp.KeyIndicator()[5] / ...
    hwp.Run("MoveParaBegin")
    try:
        a = hwp.KeyIndicator()[5]
    except Exception as e:
        log(f"  KeyIndicator err (begin): {e}")
        a = -1
    hwp.Run("MoveParaEnd")
    try:
        b = hwp.KeyIndicator()[5]
    except Exception as e:
        log(f"  KeyIndicator err (end): {e}")
        b = a
    hwp.Run("MoveParaBegin")
    log(f"  [line] a={a} b={b}")

    if a == b:
        log("  → 한 줄짜리 문단, skip")
        hwp.Run("MoveNextParaBegin")
        return

    # tool1 외 추가: 진단용으로 문단 전체 텍스트 한 번 추출
    # ※ GetText 는 고정폭빈칸 등 컨트롤을 제외해 반환 (한컴 공식 확인) —
    #   이 텍스트에는 우리가 BulletRenderer 에서 넣은 fixed-width space 가
    #   안 보임. marker 검증 시 빈 워드(= FWS only) 자동 건너뜀 필요.
    hwp.Run("MoveSelParaEnd")
    full = _block_char(hwp)
    hwp.Run("Cancel")
    hwp.Run("MoveParaBegin")
    log(f"  [para] full={full!r}")

    # 첫 텍스트 워드 검색 — FWS 만 든 빈 워드(GetText 가 '' 반환) 들을 건너뛰며
    # 처음으로 만나는 진짜 텍스트 워드를 찾아 marker 검증.
    is_marker = False
    first_text_word: Optional[str] = None
    for trial in range(10):
        hwp.Run("MoveSelNextWord")
        c_raw = _block_char(hwp)
        stripped = c_raw.strip()
        log(f"  [skip-search#{trial}] raw={c_raw!r} stripped={stripped!r}")
        if stripped == "":
            # FWS / 컨트롤 only — 다음 워드 시도
            hwp.Run("Cancel")
            continue
        first_text_word = stripped
        is_marker = _is_bullet_or_annotation_marker(stripped)
        log(f"  [first-text] word={stripped!r} is_marker={is_marker}")
        hwp.Run("Cancel")
        break
    else:
        log("  → 10 시도 후 텍스트 워드 못 찾음")

    if not is_marker:
        log("  → marker 아님 (또는 빈 문단), skip")
        hwp.Run("MoveNextParaBegin")
        return

    # tool1 본문 검색 — 빈 워드(FWS only, GetText '' 반환) 도 자동 건너뜀.
    # `if not c_raw: break` 제거 — 빈 워드는 _is_body_word("")=False 로
    # 처리되어 다음 iter 진행. 무한 루프는 max_iter 30 으로 제한.
    # 진행 멈춤(MoveSelNextWord 가 캐럿 못 옮기는 케이스) 은 GetPos 비교로 감지.
    hwp.Run("MoveParaBegin")
    hwp.Run("MoveSelNextWord")
    c_raw = _block_char(hwp)
    c = c_raw[:-1] if c_raw else ""
    log(f"  [body-search#0] raw={c_raw!r} after[:-1]={c!r} body={_is_body_word(c)}")

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
            log(f"  → 캐럿 진행 멈춤 (pos {cur_pos!r} 반복), 끝")
            break
        last_pos = cur_pos
        c_raw = _block_char(hwp)
        c = c_raw[:-1] if c_raw else ""
        iters += 1
        log(f"  [body-search#{iters}] raw={c_raw!r} after[:-1]={c!r} body={_is_body_word(c)}")

    log(f"  [loop] body 발견={found} (iters={iters})")
    if not found:
        log("  → 본문 워드 못 찾음")
        hwp.Run("Cancel")
        hwp.Run("MoveNextParaBegin")
        return

    # tool1: hwp.Run("MoveWordBegin"); hwp.Run("ParagraphShapeIndentAtCaret")
    hwp.Run("MoveWordBegin")
    pos_at = hwp.GetPos()
    log(f"  [indent] IndentAtCaret 호출 직전 pos={pos_at!r}")
    hwp.Run("ParagraphShapeIndentAtCaret")
    log("  [indent] ✔ ParagraphShapeIndentAtCaret 호출됨")

    hwp.Run("MoveNextParaBegin")


def _process_objects(hwp: Any, log: LogFn) -> None:
    """
    tool1 super_shift_tab_object 포트 — 본문(list=0) 외 영역 (표 셀 등) 의
    모든 list ID 를 순회하며 _process_paragraph 호출.

    `MoveDocBegin/End` 만으로는 표 셀 안 문단을 못 다룸 (배치 모드에서
    결론 박스, 참고 박스 안 들여쓰기 적용 X 의 원인). area 변수를 1 부터
    증가시키며 SetPos(area, 0, 0) 으로 점프 — area 가 없는 list 면 한/글이
    list=0 으로 fallback 하므로 그걸 종료 조건으로 활용.
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
            break  # 더 이상 area 없음 (본문으로 fallback)

        log(f"  [object area={area}] cur={cur!r}")
        # 이 area 의 모든 문단 처리. _process_paragraph 가 다음 문단으로 이동.
        # area 는 진행하면서 동적으로 갱신될 수 있음 (셀 가로지름).
        inner_iters = 0
        while inner_iters < 1000:
            start = hwp.GetPos()
            _process_paragraph(hwp, log)
            new = hwp.GetPos()
            if new[0] != 0 and new[0] >= area:
                area = new[0]
            if new == start:
                break
            inner_iters += 1
        obj_count += 1
    log(f"  [object] 처리된 area: {obj_count}")


def align_left_indent(hwp: Any, log: LogFn = None) -> None:
    """
    tool1 super_shift_tab + super_shift_tab_object 포트 — 본문 + 표 셀 등
    객체 영역까지 모두 순회.
    """
    log = log or _noop_log

    # 1) 본문 (list=0) 순회
    log("[align_left_indent] 본문 순회 시작")
    hwp.Run("MoveDocEnd")
    end_pos = hwp.GetPos()
    hwp.Run("MoveDocBegin")

    max_iter = 100_000
    iters = 0
    while iters < max_iter:
        prev = hwp.GetPos()
        if prev == end_pos:
            break
        _process_paragraph(hwp, log)
        cur = hwp.GetPos()
        if cur == prev:
            log("  [본문 STOP] 진행 멈춤")
            break
        iters += 1
    log(f"[align_left_indent] 본문 처리 완료 ({iters} 문단)")

    # 2) 객체 영역 (표 셀 등 list >= 2) 순회 — 결론 박스·참고 박스 안 정렬용
    log("[align_left_indent] 객체 영역 순회 시작")
    _process_objects(hwp, log)
    log("[align_left_indent] 객체 영역 처리 완료")


def align_current_paragraph(hwp: Any, log: LogFn = None) -> None:
    """
    selection 이 있으면 범위 내 모든 문단에, 없으면 현재 캐럿 문단 1 개에
    들여쓰기 정렬 적용.
    """
    log = log or _noop_log
    log("[align_current_paragraph] 시작")
    apply_per_paragraph(hwp, _process_paragraph, log)
    log("[align_current_paragraph] 완료")
