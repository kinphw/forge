"""
font_battery.py — 한/글 2010 (개발 PC) 에 attach 해 폰트 적용 가능성을
일괄 진단. 각 face × FontType 조합 호출 후 readback.

사용:
    python scripts/font_battery.py

사용자 영향:
- 한/글 선택 영역이 있으면 마지막 시도의 폰트로 그 영역이 바뀝니다.
  Ctrl+Z 로 되돌릴 수 있습니다.
- 선택 없이 캐럿만 있으면 next-char default 만 바뀜 (시각 변화 없음).
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from forge.com_helpers import set_param
from forge.hwp_session import (
    init_com_for_thread,
    list_hwp_instances,
    attach_to_instance,
)
from forge.renderers.primitives import set_font, set_font_humanmyongjo


def readback_face(hwp) -> tuple[str, int, int]:
    """현재 CharShape 의 (FaceNameHangul, FontTypeHangul, Height) 반환."""
    hwp.HAction.GetDefault("CharShape", hwp.HParameterSet.HCharShape.HSet)
    cs = hwp.HParameterSet.HCharShape
    return (
        str(cs.FaceNameHangul or ""),
        int(cs.FontTypeHangul or 0),
        int(cs.Height or 0),
    )


def try_face_type(hwp, face: str, font_type: int, size_pt: float = 15.0) -> None:
    """face × FontType 조합 1 회 시도 + readback."""
    try:
        set_param(hwp, "CharShape", {
            "FaceNameHangul":   face, "FontTypeHangul":   font_type,
            "FaceNameLatin":    face, "FontTypeLatin":    font_type,
            "FaceNameUser":     face, "FontTypeUser":     font_type,
            "FaceNameSymbol":   face, "FontTypeSymbol":   font_type,
            "FaceNameOther":    face, "FontTypeOther":    font_type,
            "FaceNameJapanese": face, "FontTypeJapanese": font_type,
            "FaceNameHanja":    face, "FontTypeHanja":    font_type,
            "Height":           int(size_pt * 100),
        })
        rb_face, rb_type, rb_h = readback_face(hwp)
        ft_label = {0: "don't care", 1: "TTF", 2: "HFT"}.get(font_type, "?")
        rb_label = {0: "?", 1: "TTF", 2: "HFT"}.get(rb_type, "?")
        match = "✓" if rb_face == face else ("∅" if not rb_face else "≠")
        print(f"  {match} face={face!r:<22} ft={font_type}({ft_label:<10})  "
              f"→ readback face={rb_face!r:<22} ft={rb_type}({rb_label}) h={rb_h}")
    except Exception as e:
        print(f"  ✘ face={face!r} ft={font_type}: {type(e).__name__}: {e}")


def main() -> int:
    print("[1/4] 한/글 attach")
    init_com_for_thread()
    insts = list_hwp_instances()
    if not insts:
        print("  떠 있는 한/글 없음")
        return 2
    print(f"  인스턴스 {len(insts)}개. 첫 번째에 attach.")
    session = attach_to_instance(insts[0])
    hwp = session.hwp

    print("\n[2/4] GetFontList — 한/글이 인식한 face 목록")
    try:
        hwp.ScanFont()
        for lang_id, lang_name in [(0, "한글"), (1, "영문")]:
            s = str(hwp.GetFontList(lang_id) or "")
            print(f"  [{lang_name}] {s!r}")
    except Exception as e:
        print(f"  ScanFont/GetFontList 실패: {e}")

    print("\n[3/4] tool2 권위 헬퍼 / dispatch path")
    try:
        set_font_humanmyongjo(hwp, 15.0)
        rb = readback_face(hwp)
        print(f"  set_font_humanmyongjo(15)        → readback {rb}")
    except Exception as e:
        print(f"  set_font_humanmyongjo 실패: {e}")
    try:
        set_font(hwp, "휴먼명조", 15.0)
        rb = readback_face(hwp)
        print(f"  set_font(휴먼명조, 15)           → readback {rb}")
    except Exception as e:
        print(f"  set_font('휴먼명조') 실패: {e}")

    print("\n[4/4] face × FontType matrix")
    candidates = ["휴먼명조", "HY헤드라인M", "HY울릉도M",
                  "함초롬바탕", "맑은 고딕", "HY견명조"]
    for face in candidates:
        print(f"\n--- {face} ---")
        for ft in (0, 1, 2):
            try_face_type(hwp, face, ft, 15.0)

    return 0


if __name__ == "__main__":
    sys.exit(main())
