"""
selection 범위 / 단일 캐럿 분기 헬퍼.

탭 ③ 의 "들여쓰기 정렬", "자간조정", "자간→들여쓰기" 버튼 3 종이 모두 같은
규칙 — selection 이 있으면 범위 내 모든 문단 순회, 없으면 캐럿이 위치한
현재 문단 1 개에만 적용.

이 모듈이 그 분기 로직을 단일 함수로 제공해 3 곳에서 재사용 (디버깅·유지
보수 용이성).
"""
from __future__ import annotations

from typing import Any, Callable, Optional, Tuple

LogFn = Optional[Callable[[str], None]]
PosT = Tuple[int, int, int]  # (list, para, pos)


def _noop_log(msg: str) -> None:
    pass


def selection_range(hwp: Any) -> Optional[Tuple[PosT, PosT]]:
    """
    현재 selection 의 시작·끝 위치를 (list, para, pos) 페어로 반환.
    selection 없거나 시작==끝이면 None.

    ★ 호출 전략 — `GetSelectedPosBySet` (한컴 공식 권고).
      한컴 공식 (HwpAutomation_2504.txt p.34) 의 GetSelectedPos Remark:
        "매개변수로 포인터를 사용하므로, 포인터를 사용할 수 없는 언어에서는
         사용이 불가능 하다. 포인터를 사용하지 않는 언어를 지원하기 위해서
         ParameterSet을 사용하는 GetSelectedPosBySet()이 존재한다."

      Python 은 그 "포인터 미지원 언어" 에 해당. 무인자 `GetSelectedPos()`
      호출은 한/글 2010 (버전 80) 등에서 "필수 매개변수입니다" (-2147352561)
      에러를 raise 하므로 일관 동작 안 함. ListParaPos ParameterSet 두 개를
      넘기는 BySet 호출이 모든 버전에서 정상 동작.

      사용자 환경(한/글 2010, 모니커 `!HwpObject.80.1`)에서 2026-04-28 검증.

    Returns:
      (start, end) 페어 또는 None. start/end 는 (list, para, pos) 정수 3-tuple.
    """
    try:
        sset = hwp.CreateSet("ListParaPos")
        eset = hwp.CreateSet("ListParaPos")
        ok = hwp.GetSelectedPosBySet(sset, eset)
    except Exception:
        return None
    if not ok:
        return None

    try:
        start: PosT = (
            int(sset.Item("List")), int(sset.Item("Para")), int(sset.Item("Pos")),
        )
        end: PosT = (
            int(eset.Item("List")), int(eset.Item("Para")), int(eset.Item("Pos")),
        )
    except Exception:
        return None

    if start == end:
        return None
    return start, end


def apply_per_paragraph(
    hwp: Any,
    fn: Callable[[Any, LogFn], None],
    log: LogFn = None,
) -> None:
    """
    selection 이 있으면 범위 내 모든 문단을 순회하며 fn 호출, 없으면 현재
    문단 1 개만 호출.

    fn 의 contract: 한 문단 처리 후 캐럿이 다음 문단 시작 부근으로 이동
    (indent_align._process_paragraph / kerning._adjust_paragraph 모두 충족).

    selection 종료 조건:
      - 캐럿이 끝 문단을 넘으면 stop
      - 다른 list (= 표 셀 변경 등) 진입 시 stop — 같은 list 내만 처리
      - 진행 멈춤 (GetPos 동일 반복) 시 stop
    """
    log = log or _noop_log

    sel = selection_range(hwp)
    if sel is None:
        log("  [range] 단일 캐럿 — 현재 문단만")
        fn(hwp, log)
        return

    start, end = sel
    log(f"  [range] selection: {start} → {end}")

    # selection 해제 후 시작 위치로 이동
    hwp.Run("Cancel")
    hwp.SetPos(*start)

    end_list, end_para = end[0], end[1]
    max_iter = 1000
    iters = 0
    processed = 0

    while iters < max_iter:
        prev = hwp.GetPos()
        cur_list, cur_para = prev[0], prev[1]

        if cur_list != end_list:
            log(f"  [range] list 변경 ({cur_list} ≠ {end_list}) — 종료")
            break
        if cur_para > end_para:
            log(f"  [range] end 문단({end_para}) 초과 — 종료")
            break

        log(f"  [range#{iters}] 문단 (list={cur_list}, para={cur_para}) 처리")
        fn(hwp, log)
        processed += 1

        new = hwp.GetPos()
        if new == prev:
            log("  [range] 진행 멈춤 — 종료")
            break
        iters += 1

    log(f"  [range] 처리된 문단: {processed} 개")
