"""
font_probe.py — 한/글 활성 인스턴스에 폰트 적용 + readback 진단.

사용법:
    python scripts/font_probe.py
    python scripts/font_probe.py "휴먼명조" "TH휴먼명조" "함초롬바탕" "HY신명조"

기본 후보 4개. 인자로 임의 이름들을 추가/대체 가능.

동작:
  1. 떠 있는 한/글 인스턴스에 attach (forge.hwp_session 의 ROT enum)
  2. 각 후보 폰트마다:
     - CharShape 의 7-언어면 FaceName + Height(15pt) 설정
     - GetDefault 로 readback
     - "요청 → readback" 비교
  3. 표 형태 출력. 일치 = 한/글이 그 이름으로 인식한 것 = 사용 가능.
"""
from __future__ import annotations

import sys
from pathlib import Path

# forge 패키지 import 가능하게
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from forge.com_helpers import set_param
from forge.hwp_session import (
    MultipleHwpInstancesError,
    NoExistingHwpError,
    attach_or_create,
    init_com_for_thread,
    list_hwp_instances,
    attach_to_instance,
)


DEFAULT_CANDIDATES = [
    "휴먼명조",
    "TH휴먼명조",
    "함초롬바탕",
    "HY신명조",
    "맑은 고딕",     # 컨트롤 — 동작 확인용
]


def attach() -> object:
    init_com_for_thread()
    insts = list_hwp_instances()
    if not insts:
        raise NoExistingHwpError(
            "떠 있는 한/글 인스턴스 없음 — 한/글 먼저 띄워주세요."
        )
    if len(insts) == 1:
        s = attach_to_instance(insts[0])
        return s.hwp
    # 여러 개면 첫 번째 사용 (진단 스크립트라 단순 처리)
    print(f"[info] {len(insts)}개 인스턴스 중 첫 번째에 attach")
    s = attach_to_instance(insts[0])
    return s.hwp


def probe_font(hwp, name: str, size_pt: float = 15.0) -> dict:
    """폰트 1개 시도. 결과 dict 반환."""
    height = int(size_pt * 100)
    try:
        set_param(hwp, "CharShape", {
            "FaceNameUser":     name, "FontTypeUser":     1,
            "FaceNameHangul":   name, "FontTypeHangul":   1,
            "FaceNameSymbol":   name, "FontTypeSymbol":   1,
            "FaceNameOther":    name, "FontTypeOther":    1,
            "FaceNameJapanese": name, "FontTypeJapanese": 1,
            "FaceNameHanja":    name, "FontTypeHanja":    1,
            "FaceNameLatin":    name, "FontTypeLatin":    1,
            "Height":           height,
        })
        apply_err = ""
    except Exception as e:
        apply_err = f"{type(e).__name__}: {e}"

    # readback
    try:
        hwp.HAction.GetDefault("CharShape", hwp.HParameterSet.HCharShape.HSet)
        cs = hwp.HParameterSet.HCharShape
        face_h = str(cs.FaceNameHangul or "")
        face_l = str(cs.FaceNameLatin or "")
        face_u = str(cs.FaceNameUser or "")
        h_back = int(cs.Height or 0)
        rd_err = ""
    except Exception as e:
        face_h = face_l = face_u = ""
        h_back = 0
        rd_err = f"{type(e).__name__}: {e}"

    match = (face_h == name)
    return {
        "name":   name,
        "apply":  apply_err or "ok",
        "h_back": h_back,
        "h_req":  height,
        "f_h":    face_h,
        "f_l":    face_l,
        "f_u":    face_u,
        "match":  match,
        "rd_err": rd_err,
    }


def main(args: list[str]) -> int:
    candidates = args or DEFAULT_CANDIDATES
    print(f"[font_probe] 후보 {len(candidates)}개: {candidates}")
    print()

    try:
        hwp = attach()
    except (NoExistingHwpError, MultipleHwpInstancesError) as e:
        print(f"[ERROR] {e}")
        return 2
    except Exception as e:
        print(f"[ERROR] attach 실패: {type(e).__name__}: {e}")
        return 3

    print(f"  {'요청':<14}  {'readback':<14}  {'Height':<14} {'결과'}")
    print(f"  {'-'*14}  {'-'*14}  {'-'*14} {'-'*4}")
    results = []
    for name in candidates:
        r = probe_font(hwp, name)
        results.append(r)
        match_mark = "✓ 일치" if r["match"] else ("∅ 빈값" if not r["f_h"] else "≠ 다름")
        h_str = f"{r['h_back']}/{r['h_req']}"
        print(f"  {r['name']:<14}  {r['f_h']:<14}  {h_str:<14} {match_mark}")
        if r["apply"] != "ok":
            print(f"      apply 오류: {r['apply']}")
        if r["rd_err"]:
            print(f"      readback 오류: {r['rd_err']}")

    print()
    ok = [r["name"] for r in results if r["match"]]
    if ok:
        print(f"[결과] 한/글이 인식한 폰트 ({len(ok)}개): {ok}")
        print("       → 이 이름들을 templates.py / realtime_tab.py 에 사용하면 됩니다.")
    else:
        print("[결과] 일치하는 후보 없음.")
        print("       → readback 이 모두 빈값이면 selection 문제일 수 있음 — 한/글에서")
        print("         아무 글자나 드래그해 영역 지정 후 다시 실행해 보세요.")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
