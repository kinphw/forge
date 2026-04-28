"""
한/글 attach 진단 스크립트.

사용:  python -m forge.diagnose

신규 한/글 spawn 흐름의 각 단계를 콘솔에 찍어 어디서 실패하는지 보여준다.
DRM 환경에서 'shell spawn → ROT attach' 가 안 될 때 디버깅용.

출력 항목:
  1. 환경 정보 (Python, 권한)
  2. 현재 한/글 프로세스 / ROT moniker 목록
  3. 레지스트리에서 Hwp.exe 경로 추출
  4. ShellExecute 로 Hwp.exe 실행
  5. ROT polling — 매초 새 moniker 출력
  6. attach 시도 + 결과
"""
from __future__ import annotations

import ctypes
import os
import sys
import time
from datetime import datetime


def banner(s: str) -> None:
    print()
    print("=" * 70)
    print(s)
    print("=" * 70)


def log(msg: str) -> None:
    print(f"[{datetime.now().strftime('%H:%M:%S.%f')[:-3]}] {msg}")


def main() -> int:
    banner("1. 환경 정보")
    log(f"Python: {sys.version.split()[0]}  ({sys.executable})")
    log(f"Platform: {sys.platform}")
    try:
        is_admin = ctypes.windll.shell32.IsUserAnAdmin() != 0
        log(f"Admin: {is_admin}")
    except Exception as e:
        log(f"Admin 체크 실패: {e}")

    # ── 2. 현재 한/글 프로세스
    banner("2. 현재 한/글 프로세스")
    try:
        import psutil
        found = []
        for p in psutil.process_iter(["name", "pid", "exe"]):
            try:
                name = (p.info.get("name") or "").lower()
                if name in {"hwp.exe", "hword.exe"}:
                    found.append(p.info)
            except Exception:
                continue
        if found:
            for info in found:
                log(f"PID={info['pid']}  name={info['name']}  exe={info.get('exe')}")
        else:
            log("(한/글 프로세스 없음)")
    except ImportError:
        log("psutil 미설치 — 건너뜀")

    # ── 3. 레지스트리에서 Hwp.exe 경로
    banner("3. Hwp.exe 레지스트리 경로 추출")
    from forge.hwp_session import _find_hwp_exe
    exe = _find_hwp_exe()
    if exe:
        log(f"Hwp.exe 경로: {exe}")
        log(f"파일 존재: {os.path.exists(exe)}")
    else:
        log("✘ Hwp.exe 경로 추출 실패 — 레지스트리 HWPFrame.HwpObject ProgID 미등록")
        log("  → 이 경우 ShellExecute 경로 무력화. 신규 spawn 은 CoCreate fallback 만 가능.")
        return 1

    # ── 4. 현재 ROT moniker 스냅샷
    banner("4. 현재 ROT moniker 목록")
    import pythoncom
    pythoncom.CoInitialize()
    from forge.hwp_session import _list_rot_monikers, _HWPOBJECT_MONIKER_RE

    before = _list_rot_monikers()
    log(f"ROT 항목 수: {len(before)}")
    hwp_monikers = [n for n in before if _HWPOBJECT_MONIKER_RE.match(n)]
    if hwp_monikers:
        log(f"기존 HwpObject moniker: {hwp_monikers}")
    else:
        log("(HwpObject moniker 없음 — 신규 spawn 필요 상태)")

    # ── 5. ShellExecute 로 Hwp.exe 실행
    banner("5. ShellExecute 로 Hwp.exe 실행 (os.startfile)")
    log(f"실행: {exe}")
    try:
        os.startfile(exe)
        log("✔ os.startfile 호출 성공 (반환됨)")
    except OSError as e:
        log(f"✘ os.startfile 실패: {e}")
        return 1

    # ── 6. Polling — 30초간 매초 ROT 변화 감시
    banner("6. ROT polling (30 초, 매초)")
    deadline = time.time() + 30
    found_moniker = None
    seen_history: set[str] = set(before)
    iteration = 0
    while time.time() < deadline:
        iteration += 1
        time.sleep(1.0)
        current = _list_rot_monikers()
        new = current - seen_history
        seen_history |= new
        new_hwp = [n for n in new if _HWPOBJECT_MONIKER_RE.match(n)]
        if new:
            log(f"  iter {iteration}: 신규 moniker {len(new)} 개 등장 — "
                f"HwpObject: {new_hwp}, 기타: {len(new) - len(new_hwp)}")
        if new_hwp:
            found_moniker = new_hwp[0]
            log(f"✔ HwpObject moniker 감지: {found_moniker}")
            break

    if not found_moniker:
        log("✘ 30 초 내 HwpObject moniker 미등장")
        log(f"  최종 ROT 항목 수: {len(_list_rot_monikers())}")
        log("  → DRM 이 Hwp.exe 시작은 허용했지만 COM/ROT 등록을 차단할 수 있음.")
        log("  → 또는 Hwp.exe 가 아직 초기화 중일 수 있음. timeout 늘려 재시도 권장.")
        # 그래도 한/글 프로세스가 떴는지 확인
        try:
            import psutil
            still = [p.info for p in psutil.process_iter(["name", "pid"])
                     if (p.info.get("name") or "").lower() in {"hwp.exe", "hword.exe"}]
            log(f"  한/글 프로세스 현재: {len(still)} 개 — {still}")
        except Exception:
            pass
        return 1

    # ── 7. 실제 attach
    banner("7. attach 시도 (_find_in_rot)")
    from forge.hwp_session import _find_in_rot
    result = _find_in_rot()
    if result is None:
        log("✘ _find_in_rot None — moniker 는 보였지만 GetObject/QueryInterface 실패")
        return 1
    hwp, name, vcode, idx = result
    log(f"✔ attach 성공: moniker={name}  버전코드={vcode}  index={idx}")
    try:
        log(f"  XHwpWindows.Count = {hwp.XHwpWindows.Count}")
        log(f"  Version = {hwp.Version}")
    except Exception as e:
        log(f"  속성 조회 실패: {e}")

    banner("✔ 진단 완료 — shell spawn 정상 작동")
    return 0


if __name__ == "__main__":
    sys.exit(main())
