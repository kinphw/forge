"""
diag_rot.py — ROT 전체 entry + 한/글 프로세스 dump.

Forge 가 한/글 2018 인스턴스를 못 찾는 원인 진단:
1. ROT 에 어떤 항목이 등록되어 있는지 전부 dump
2. 한/글로 보이는 항목 (HwpObject 정규식) 매칭 결과
3. 프로세스 list 에서 hwp.exe 류 — PID + 실행 파일 경로 + 권한 (IL)
4. Forge 의 list_hwp_instances() 결과
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))


def main() -> int:
    print("=" * 70)
    print("[1] 한/글 프로세스 (psutil)")
    print("=" * 70)
    try:
        import psutil
    except ImportError:
        print("  psutil 미설치 — pip install psutil")
        return 1

    me = psutil.Process()
    print(f"  나(이 스크립트): pid={me.pid} exe={me.exe()}")
    found_hwp = []
    for p in psutil.process_iter(["name", "pid", "exe", "username"]):
        try:
            name = (p.info.get("name") or "").lower()
            if "hwp" in name or "hword" in name or "hcell" in name or "hshow" in name:
                exe = p.info.get("exe") or "?"
                user = p.info.get("username") or "?"
                print(f"  pid={p.pid:>6} name={name!r:<14} user={user!r:<25} exe={exe}")
                found_hwp.append(p.pid)
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue
    if not found_hwp:
        print("  (한/글류 프로세스 없음)")

    print()
    print("=" * 70)
    print("[2] ROT (Running Object Table) 전체 dump")
    print("=" * 70)
    import pythoncom
    pythoncom.CoInitialize()
    ctx = pythoncom.CreateBindCtx(0)
    rot = pythoncom.GetRunningObjectTable()
    all_names = []
    for mk in rot:
        try:
            name = mk.GetDisplayName(ctx, None)
        except Exception as e:
            print(f"  (display name 조회 실패: {e})")
            continue
        all_names.append(name)
    if not all_names:
        print("  (ROT 가 비어있음 — 다른 IL 의 한/글이거나 ROT 미등록 상태)")
    else:
        for name in all_names:
            print(f"  {name!r}")

    print()
    print("=" * 70)
    print("[3] HwpObject 정규식 매칭")
    print("=" * 70)
    import re
    pat = re.compile(r"^!HwpObject\.(\d+)\.(\d+)$")
    matched = [n for n in all_names if pat.match(n)]
    if matched:
        for n in matched:
            m = pat.match(n)
            ver, idx = int(m.group(1)), int(m.group(2))
            print(f"  ✓ {n!r}  버전코드={ver} 인덱스={idx}")
    else:
        print("  ✘ 매칭 entry 없음")
        # 부분 매칭으로 hint
        hwp_like = [n for n in all_names if "hwp" in n.lower()]
        if hwp_like:
            print("  부분 매칭 (hwp 포함):")
            for n in hwp_like:
                print(f"    {n!r}")

    print()
    print("=" * 70)
    print("[4] Forge.list_hwp_instances() 호출")
    print("=" * 70)
    from forge.hwp_session import list_hwp_instances
    try:
        insts = list_hwp_instances()
        if insts:
            for i, inst in enumerate(insts):
                print(f"  [{i}] {inst.display_label}  moniker={inst.moniker_name!r}")
        else:
            print("  ✘ list_hwp_instances() 빈 결과")
    except Exception as e:
        print(f"  ✘ 호출 실패: {type(e).__name__}: {e}")

    print()
    print("=" * 70)
    print("[5] 직접 EnsureDispatch 시도 (fallback path)")
    print("=" * 70)
    try:
        from win32com.client import gencache
        hwp = gencache.EnsureDispatch("HWPFrame.HwpObject")
        print(f"  ✓ EnsureDispatch 성공: {hwp}")
        try:
            print(f"    XHwpWindows.Count = {hwp.XHwpWindows.Count}")
            print(f"    Path = {hwp.Path!r}")
            print(f"    Version = {hwp.Version!r}")
        except Exception as e:
            print(f"    속성 접근 실패: {e}")
        # 이후 ROT 새로 dump — 우리가 만든 인스턴스가 등록됐는지
        print("\n  EnsureDispatch 직후 ROT 재조회:")
        rot2 = pythoncom.GetRunningObjectTable()
        for mk in rot2:
            try:
                nm = mk.GetDisplayName(ctx, None)
                if "hwp" in nm.lower():
                    print(f"    {nm!r}")
            except Exception:
                continue
    except Exception as e:
        print(f"  ✘ EnsureDispatch 실패: {type(e).__name__}: {e}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
