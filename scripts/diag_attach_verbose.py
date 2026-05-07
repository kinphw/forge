"""
diag_attach_verbose.py — list_hwp_instances 의 silent fail 지점 추적.

각 단계 (rot.GetObject / QueryInterface / EnsureDispatch / is_alive) 결과
+ 예외 traceback 을 그대로 출력해 진짜 원인 노출.
"""
from __future__ import annotations

import sys
import traceback
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))


def main() -> int:
    print("[1] CoInitialize")
    import pythoncom
    pythoncom.CoInitialize()

    print("\n[2] ROT enum + HwpObject 매칭 — 단계별 상세")
    import re
    pat = re.compile(r"^!HwpObject\.(\d+)\.(\d+)$")
    ctx = pythoncom.CreateBindCtx(0)
    rot = pythoncom.GetRunningObjectTable()

    from win32com.client import gencache, Dispatch
    for mk in rot:
        try:
            name = mk.GetDisplayName(ctx, None)
        except Exception as e:
            continue
        if not pat.match(name):
            continue
        print(f"\n  --- moniker: {name!r} ---")
        # (a) GetObject
        try:
            obj = rot.GetObject(mk)
            print(f"  (a) rot.GetObject 성공: {type(obj).__name__}")
        except Exception:
            print("  (a) rot.GetObject 실패:")
            traceback.print_exc()
            continue
        # (b) QueryInterface(IDispatch)
        try:
            disp = obj.QueryInterface(pythoncom.IID_IDispatch)
            print(f"  (b) QueryInterface(IDispatch) 성공: {type(disp).__name__}")
        except Exception:
            print("  (b) QueryInterface 실패:")
            traceback.print_exc()
            continue
        # (c1) gencache.EnsureDispatch(disp)
        try:
            hwp = gencache.EnsureDispatch(disp)
            print(f"  (c1) gencache.EnsureDispatch(disp) 성공: {type(hwp).__name__}")
        except Exception:
            print("  (c1) gencache.EnsureDispatch(disp) 실패 — gencache typelib 부재?")
            traceback.print_exc()
            print("\n  (c2) plain Dispatch(disp) 로 재시도 — typelib 없이 late-bound")
            try:
                hwp = Dispatch(disp)
                print(f"  (c2) Dispatch(disp) 성공: {type(hwp).__name__}")
            except Exception:
                print("  (c2) Dispatch(disp) 도 실패:")
                traceback.print_exc()
                continue
        # (d) is_alive 체크 — XHwpWindows.Count
        try:
            cnt = hwp.XHwpWindows.Count
            print(f"  (d) is_alive: XHwpWindows.Count = {cnt}")
        except Exception:
            print("  (d) is_alive 실패:")
            traceback.print_exc()
            continue
        # (e) 가능하면 추가 정보
        try:
            print(f"      Path    = {str(hwp.Path or '')!r}")
        except Exception as e:
            print(f"      Path 접근 실패: {e}")
        try:
            print(f"      Version = {hwp.Version!r}")
        except Exception as e:
            print(f"      Version 접근 실패: {e}")

    print("\n[3] 레지스트리 ProgID 'HWPFrame.HwpObject' 검사")
    import winreg
    keys_to_check = [
        r"HWPFrame.HwpObject",
        r"HWPFrame.HwpObject.1",
        r"HWPFrame.HwpCtrl",
        r"Hwp.Application",
        r"HOffice.Application",
    ]
    for kn in keys_to_check:
        try:
            with winreg.OpenKey(winreg.HKEY_CLASSES_ROOT, kn) as k:
                clsid = ""
                try:
                    with winreg.OpenKey(k, "CLSID") as ck:
                        clsid = winreg.QueryValueEx(ck, "")[0]
                except OSError:
                    pass
                default = ""
                try:
                    default = winreg.QueryValueEx(k, "")[0]
                except OSError:
                    pass
            print(f"  ✓ HKCR\\{kn}  default={default!r}  CLSID={clsid!r}")
        except OSError:
            print(f"  ✘ HKCR\\{kn}  (없음)")

    print("\n[4] 한/글 typelib (gen_py) 캐시 확인")
    try:
        from win32com.client import gen_py
        gen_dir = Path(gen_py.__path__[0])
        print(f"  gen_py dir: {gen_dir}")
        hwp_files = sorted(gen_dir.glob("*HwpObject*"))
        for f in hwp_files:
            print(f"    {f.name}")
        if not hwp_files:
            print("    (HwpObject typelib 캐시 없음 — gencache 가 신규 생성 시도해야 함)")
    except Exception as e:
        print(f"  gen_py 조회 실패: {e}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
