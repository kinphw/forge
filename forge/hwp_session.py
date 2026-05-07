r"""
한/글 COM 인스턴스 attach·신규 생성 헬퍼.

연결 정책 (한컴 공식 답변 기반):
  1. pythoncom 으로 ROT (Running Object Table) enumerate
  2. moniker 이름이 `!HwpObject.{버전}.{인덱스}` 패턴인 항목 탐색
     버전 코드 ↔ 한/글 출시 버전 (한컴 공식 답변):
        80  = 한/글 2010
        90  = 한/글 2014
        96  = 한컴오피스 NEO
        100 = 한/글 2018
        110 = 한/글 2020
        120 = 한/글 2022
        130 = 한/글 2024
     인덱스: 1~99, 같은 버전이 여러 개 떠 있을 때 부여
  3. 발견 시: GetObject → QueryInterface(IID_IDispatch) → EnsureDispatch(disp)
     로 기존 인스턴스 attach
  4. 없으면 (신규 spawn 정책 — DRM 환경 우선):
     a) 레지스트리에서 Hwp.exe 절대 경로 확보 → os.startfile (ShellExecute)
        로 사용자 클릭과 동등하게 실행 → ROT 등록 polling 후 attach.
        Fasoo 등 사내 DRM 이 자격증명 inject 가능 (CoCreate 는 부모=python
        이라 inject 회피됨 → 생성 문서 편집 불가).
     b) 실패 시 fallback: gencache.EnsureDispatch("HWPFrame.HwpObject")
        (DRM 미적용 환경에서만 정상)

★ 정규식 패턴은 위 모든 버전 코드를 동일하게 매칭 (\d+ 사용) — 새 버전이
나와도 코드 변경 없이 작동. 매칭된 버전 코드는 HwpSession 에 보관하여
status 메시지·로그에 노출.

제약: ROT 는 동일 integrity level 안에서만 enumerate 가능 (Windows COM 정책).
즉 Forge 가 High IL (admin) 로 실행되면 Medium IL (일반) 사용자 한/글에는
attach 불가. 폐쇄망 일반 사용 시나리오에서는 보통 둘 다 Medium IL 이라 작동.
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Any, Optional

# 한/글 ROT moniker 패턴 — 한컴 공식 답변 기준
# `!HwpObject.{버전}.{인덱스}` — 버전·인덱스를 capture 해서 식별 정보로 활용
_HWPOBJECT_MONIKER_RE = re.compile(r"^!HwpObject\.(\d+)\.(\d+)$")

# 버전 코드 ↔ 출시 버전 이름 (한컴 공식 답변)
# 알 수 없는 코드는 "한/글 v{code}" 로 fallback (새 버전 출시 대응)
_HWP_VERSION_NAMES: dict[int, str] = {
    80:  "한/글 2010",
    90:  "한/글 2014",
    96:  "한컴오피스 NEO",
    100: "한/글 2018",
    110: "한/글 2020",
    120: "한/글 2022",
    130: "한/글 2024",
}

# 한/글 실행파일 후보 (대소문자 무시 비교)
_HWP_PROCESS_NAMES = {"hwp.exe", "hword.exe"}


def hwp_version_name(version_code: Optional[int]) -> str:
    """버전 코드를 사람이 읽는 이름으로. 미상은 일반 fallback."""
    if version_code is None:
        return "한/글 (버전 미상)"
    return _HWP_VERSION_NAMES.get(version_code, f"한/글 v{version_code}")


@dataclass
class HwpSession:
    """한/글 COM 인스턴스 wrapper."""

    hwp: Any                          # win32com.client COM 객체 (HWPFrame.HwpObject)
    is_new: bool                      # 우리가 새로 spawn 했으면 True
    pre_existing: bool = False        # attach 시점에 다른 한/글이 떠 있었지만 attach 불가했음
    process_pid: Optional[int] = None # 한/글 프로세스 PID (가능하면)
    version_code: Optional[int] = None  # ROT moniker 의 버전 코드 (80/90/96/100/...)
    instance_index: Optional[int] = None  # ROT moniker 의 인덱스 (1~99)
    moniker_name: Optional[str] = None    # 원본 moniker 문자열 (디버깅용)

    @property
    def version_name(self) -> str:
        """사람이 읽는 한/글 버전명."""
        return hwp_version_name(self.version_code)


@dataclass
class HwpInstance:
    """
    ROT 에서 발견된 한/글 인스턴스 1 개 (선택 UI 용).

    `list_hwp_instances()` 로 후보를 수집하고, 사용자가 고른 인스턴스를
    `attach_to_instance()` 로 정식 HwpSession 으로 승격한다. moniker
    문자열이 식별자 역할 — 동일 PC 에 같은 버전 한/글이 여러 개 떠 있어도
    `instance_index` 로 구분.
    """
    hwp: Any                          # 이미 살아있는 것이 검증된 COM 객체
    moniker_name: str
    version_code: int
    instance_index: int
    active_file_path: str             # 활성 문서 전체 경로. 새 문서/저장 안 됨 = ""

    @property
    def version_name(self) -> str:
        return hwp_version_name(self.version_code)

    @property
    def display_label(self) -> str:
        """UI 라벨 — '한/글 2024 #1 — report.hwpx' 형식."""
        import os
        if self.active_file_path:
            base = os.path.basename(self.active_file_path)
        else:
            base = "(새 문서 / 저장 안 됨)"
        return f"{self.version_name} #{self.instance_index} — {base}"


def init_com_for_thread() -> None:
    """
    COM 사용 전 현재 스레드에 대해 CoInitialize 호출.

    각 백그라운드 스레드가 COM 객체를 만들기 전 1회 호출. 누락 시
    "CoInitialize가 호출되지 않았습니다" (HRESULT 0x800401F0) 오류.
    """
    import pythoncom
    pythoncom.CoInitialize()


def _hwp_pids() -> set[int]:
    """현재 시스템의 한/글 프로세스 PID 집합."""
    pids: set[int] = set()
    try:
        import psutil
    except ImportError:
        return pids
    for p in psutil.process_iter(["name", "pid"]):
        try:
            name = (p.info.get("name") or "").lower()
            if name in _HWP_PROCESS_NAMES:
                pids.add(p.info["pid"])
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue
    return pids


def is_hwp_running() -> bool:
    """시스템에 한/글 프로세스가 떠 있는지."""
    return bool(_hwp_pids())


def is_alive(hwp: Any) -> bool:
    """
    잡고 있던 COM 객체가 아직 살아 있는지.

    한/글 GUI 가 종료된 뒤 우리 핸들로 RPC 호출하면 실패. 가벼운 속성
    접근으로 liveness 확인.
    """
    if hwp is None:
        return False
    try:
        _ = hwp.XHwpWindows.Count
        return True
    except Exception:
        return False


def _list_rot_monikers() -> set[str]:
    """ROT 의 모든 moniker display name 집합 — 신규 spawn 인스턴스 식별용."""
    import pythoncom
    ctx = pythoncom.CreateBindCtx(0)
    rot = pythoncom.GetRunningObjectTable()
    names: set[str] = set()
    for mk in rot:
        try:
            names.add(mk.GetDisplayName(ctx, None))
        except Exception:
            continue
    return names


def _find_hwp_exe() -> Optional[str]:
    """
    레지스트리에서 Hwp.exe 절대 경로 추출.

    ProgID `HWPFrame.HwpObject` → CLSID → LocalServer32 순으로 lookup.
    LocalServer32 값은 `"C:\\...\\Hwp.exe" /Automation` 같은 형식이므로
    실행 파일 경로만 잘라 반환.
    """
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_CLASSES_ROOT, r"HWPFrame.HwpObject\CLSID") as k:
            clsid, _ = winreg.QueryValueEx(k, "")
    except OSError:
        return None
    candidates = [
        (winreg.HKEY_CLASSES_ROOT, rf"CLSID\{clsid}\LocalServer32"),
        (winreg.HKEY_LOCAL_MACHINE, rf"SOFTWARE\Classes\CLSID\{clsid}\LocalServer32"),
        (winreg.HKEY_LOCAL_MACHINE, rf"SOFTWARE\Classes\WOW6432Node\CLSID\{clsid}\LocalServer32"),
    ]
    for hive, path in candidates:
        try:
            with winreg.OpenKey(hive, path) as k:
                cmd, _ = winreg.QueryValueEx(k, "")
        except OSError:
            continue
        cmd = (cmd or "").strip()
        if not cmd:
            continue
        # `"C:\...\Hwp.exe" /Automation` 또는 `C:\...\Hwp.exe /Automation`
        # 따옴표가 있으면 그 안의 경로. 없으면 `.exe` 직후를 컷오프.
        if cmd.startswith('"'):
            end = cmd.find('"', 1)
            if end > 0:
                return cmd[1:end]
        # 따옴표 없음 — 경로에 공백 (`Program Files`) 이 있을 수 있어
        # split 으로 자르면 안 됨. .exe 까지 ungreedy 매칭.
        m = re.search(r"(.+?\.exe)(?:\s|$)", cmd, re.IGNORECASE)
        if m:
            return m.group(1)
        return cmd  # 최후 fallback — 그대로 반환
    return None


def _spawn_hwp_via_shell(timeout: float = 30.0) -> Optional[tuple[Any, str, int, int]]:
    """
    ShellExecute 로 Hwp.exe 실행 후 ROT 등록 대기 → attach.

    ★ Fasoo 등 사내 DRM 회피용. CoCreateInstance 로 띄우면 부모가 python.exe
    이라 DRM 이 자격증명 inject 안 함 → 생성 문서 권한 문제 발생. os.startfile
    은 ShellExecute 경유 — 사용자가 직접 실행한 것과 동등하게 처리됨.

    실패 시 (Hwp.exe 못 찾음 / 실행 실패 / timeout) None 반환 → 호출자가
    CoCreate fallback 결정.
    """
    import os
    import time

    exe = _find_hwp_exe()
    if not exe:
        return None
    try:
        os.startfile(exe)
    except OSError:
        return None
    deadline = time.time() + timeout
    while time.time() < deadline:
        time.sleep(0.5)
        result = _find_in_rot()
        if result is not None:
            return result
    return None


def _find_in_rot(
    prefer_moniker: Optional[str] = None,
) -> Optional[tuple[Any, str, int, int]]:
    """
    ROT 에서 살아있는 HWPFrame.HwpObject 인스턴스 찾기.

    동일 integrity level 의 한/글만 enum 가능. 발견하면 IDispatch 추출 후
    gencache.EnsureDispatch(disp) 로 early-bound wrapper 만들어 반환.

    Args:
        prefer_moniker: 지정 시 그 moniker 와 정확히 일치하는 인스턴스만 반환.
            일치하는 게 ROT 에 없으면 (=종료/제거됨) None. 다른 인스턴스로
            silent fallback 하지 않음 — 사용자가 의도하지 않은 한/글에 붙는
            사고 방지.
        prefer_moniker=None: 기존 동작 유지 — 첫 매칭.

    반환: (hwp, moniker_name, version_code, instance_index) 또는 None.
    """
    import pythoncom
    from win32com.client import gencache

    ctx = pythoncom.CreateBindCtx(0)
    rot = pythoncom.GetRunningObjectTable()
    for mk in rot:
        try:
            name = mk.GetDisplayName(ctx, None)
        except Exception:
            continue
        m = _HWPOBJECT_MONIKER_RE.match(name)
        if not m:
            continue
        if prefer_moniker is not None and name != prefer_moniker:
            continue
        version_code = int(m.group(1))
        instance_index = int(m.group(2))
        try:
            obj = rot.GetObject(mk)
            disp = obj.QueryInterface(pythoncom.IID_IDispatch)
            hwp = _wrap_idispatch(disp)
        except Exception:
            continue
        # dead moniker 가 ROT 에 잔존할 수 있음 — 가벼운 호출로 검증
        if is_alive(hwp):
            return hwp, name, version_code, instance_index
    return None


def _wrap_idispatch(disp: Any) -> Any:
    """
    ROT 에서 받은 IDispatch 를 win32com 객체로 wrap.

    `gencache.EnsureDispatch(disp)` 가 early-bound wrapper 를 만들어주면
    가장 빠르지만, IDispatch 에 추적 가능한 type library 정보가 없으면
    `TypeError: This COM object can not automate the makepy process` 로
    실패 (한/글 2018·2024 등 일부 빌드에서 보고). 그 경우 late-bound
    `Dispatch(disp)` 로 fallback — dynamic dispatch 라 약간 느리지만
    한/글 COM 호출은 본질적으로 GUI 동기 호출이라 체감 차이 없음.

    참고: 신규 spawn 경로의 `EnsureDispatch("HWPFrame.HwpObject")` 는 ProgID
    로 typelib 직접 lookup 하므로 이 fallback 불필요 — 그쪽은 그대로 유지.
    """
    from win32com.client import Dispatch, gencache
    try:
        return gencache.EnsureDispatch(disp)
    except (TypeError, AttributeError):
        return Dispatch(disp)


def list_hwp_instances() -> list[HwpInstance]:
    """
    ROT 에 등록된 모든 살아있는 한/글 인스턴스를 수집.

    각 인스턴스마다 `hwp.Path` 를 읽어 활성 문서 파일 경로를 얻는다 (저장
    안 된 새 문서는 빈 문자열). 호출 스레드에 대해 CoInitialize 자동.

    UI 의 "한/글 선택" 다이얼로그가 표시할 후보 목록 생성용.
    """
    init_com_for_thread()
    import pythoncom

    ctx = pythoncom.CreateBindCtx(0)
    rot = pythoncom.GetRunningObjectTable()
    out: list[HwpInstance] = []
    for mk in rot:
        try:
            name = mk.GetDisplayName(ctx, None)
        except Exception:
            continue
        m = _HWPOBJECT_MONIKER_RE.match(name)
        if not m:
            continue
        try:
            obj = rot.GetObject(mk)
            disp = obj.QueryInterface(pythoncom.IID_IDispatch)
            hwp = _wrap_idispatch(disp)
        except Exception:
            continue
        if not is_alive(hwp):
            continue
        # 활성 문서 경로 — 실패 / 새 문서는 빈 문자열
        try:
            path = str(hwp.Path or "")
        except Exception:
            path = ""
        out.append(HwpInstance(
            hwp=hwp,
            moniker_name=name,
            version_code=int(m.group(1)),
            instance_index=int(m.group(2)),
            active_file_path=path,
        ))
    return out


def attach_to_instance(instance: HwpInstance) -> HwpSession:
    """
    `list_hwp_instances()` 결과 중 사용자가 선택한 인스턴스를 정식 세션으로 승격.

    ROT 에서 이미 살아있음을 확인하고 COM 객체를 들고 있는 상태이므로 추가
    attach 불필요. 한컴 보안 모듈만 등록하고 HwpSession 으로 wrap.

    `is_new=False` — Visible 토글 호출 안 함 (사용자가 띄운 윈도우의 크기 보존).
    """
    try:
        instance.hwp.RegisterModule("FilePathCheckDLL", "FilePathCheckerModule")
    except Exception:
        pass
    return HwpSession(
        hwp=instance.hwp,
        is_new=False,
        pre_existing=False,
        version_code=instance.version_code,
        instance_index=instance.instance_index,
        moniker_name=instance.moniker_name,
    )


class NoExistingHwpError(RuntimeError):
    """떠 있는 한/글 인스턴스가 없을 때 (allow_spawn=False 인 경우)."""


class MultipleHwpInstancesError(RuntimeError):
    """
    여러 한/글 인스턴스가 떠 있는데 사용자가 아직 선택하지 않은 상태.

    Forge 가 임의로 첫 매칭에 attach 하면 사용자가 의도하지 않은 한/글에서
    편집되는 사고 발생 (실제 보고된 증상). 이 예외를 받아 UI 가 picker
    다이얼로그를 띄워 사용자에게 명시 선택을 요구해야 함.

    `instances` 에 후보 목록을 그대로 담아 전달 → UI 는 다시 list 호출 안 해도 됨.
    """
    def __init__(self, instances: list[HwpInstance]):
        super().__init__(
            f"한/글 인스턴스가 {len(instances)}개 떠 있어 자동 선택할 수 없습니다. "
            "'한/글 선택' 버튼으로 작업할 인스턴스를 골라주세요."
        )
        self.instances = instances


def attach_or_create(
    visible: bool = True,
    allow_spawn: bool = True,
    prefer_moniker: Optional[str] = None,
) -> HwpSession:
    """
    한/글 COM 연결 — ROT attach 우선, 없으면 신규 spawn.

    Args:
        visible: 연결 후 한/글 창을 보이게 할지.
        allow_spawn:
            True (기본): 떠 있는 한/글 없으면 ShellExecute 또는 CoCreate 로
                spawn. 개발/일반 환경 기본 동작.
            False: 떠 있는 한/글이 없으면 NoExistingHwpError 즉시 raise.
                ★ Fasoo DRM + 한/글 2022 시작 화면 조합에서 자동 spawn 이
                불안정 (시작 화면이 OS 포커스 점유 → COM active doc 과
                visual window 어긋남 → 표 탈출 실패) — 운영 환경(GUI) 은
                이 모드로 호출하여 사용자에게 한/글 수동 실행을 강제.
        prefer_moniker:
            지정 시 그 moniker 와 정확히 일치하는 인스턴스만 attach 시도.
            ROT 에 그 moniker 가 없으면 NoExistingHwpError 즉시 raise (다른
            인스턴스로 silent fallback 안 함). 사용자가 명시 선택한 한/글 이
            중간에 종료된 경우 임의 한/글로 갈아타지 않게 보호.
            None (기본): 첫 매칭 인스턴스 사용 (기존 동작).

    호출 스레드에 대해 CoInitialize 자동 수행. 한/글 버전 (80=2010 ~
    130=2024) 은 모두 동일 패턴으로 매칭되므로 어떤 버전이 깔린 환경에서도
    추가 코드 변경 없이 작동.
    """
    init_com_for_thread()

    # 한/글이 시스템에 떠 있는지 사전 확인 (pre_existing 판정용)
    had_hwp_before = is_hwp_running()

    # 1. ROT enum + GetObject 로 기존 인스턴스 attach 시도
    found = _find_in_rot(prefer_moniker=prefer_moniker)
    if found is None and prefer_moniker is not None:
        # 사용자가 명시 선택한 인스턴스가 사라짐 — silent fallback 금지.
        raise NoExistingHwpError(
            f"선택하셨던 한/글 인스턴스 ({prefer_moniker}) 가 ROT 에서 사라졌습니다. "
            "한/글이 종료되었거나 보안 정책으로 ROT 등록이 해제됐을 수 있습니다. "
            "'한/글 선택' 버튼으로 다시 인스턴스를 골라주세요."
        )
    is_new = False
    if found is not None:
        hwp, moniker_name, version_code, instance_index = found
    elif not allow_spawn:
        raise NoExistingHwpError(
            "떠 있는 한/글 인스턴스를 찾지 못했습니다. "
            "한/글을 먼저 직접 실행해주세요 (빈 새 문서 또는 임의의 hwpx 파일을 연 상태)."
        )
    else:
        # 2. 신규 spawn — ShellExecute(os.startfile) 로 Hwp.exe 직접 실행 우선.
        #    ★ Fasoo 등 사내 DRM 환경 대응: CoCreateInstance(=Dispatch) 는 부모가
        #    python.exe 로 보여 DRM 이 자격증명 inject 안 함 → 생성된 hwp 권한 문제.
        #    ShellExecute 경유는 사용자 클릭과 동등 처리됨.
        spawned = _spawn_hwp_via_shell()
        if spawned is not None:
            hwp, moniker_name, version_code, instance_index = spawned
        else:
            # 3. Fallback — Hwp.exe 못 찾거나 실행 실패. 기존 CoCreate 경로.
            #    DRM 미적용 환경에서는 정상, 적용 환경에서는 권한 문제 발생 가능.
            before_monikers = _list_rot_monikers()
            from win32com.client import gencache
            hwp = gencache.EnsureDispatch("HWPFrame.HwpObject")
            moniker_name = None
            version_code = None
            instance_index = None
            new_monikers = _list_rot_monikers() - before_monikers
            for nm in new_monikers:
                m = _HWPOBJECT_MONIKER_RE.match(nm)
                if m:
                    moniker_name = nm
                    version_code = int(m.group(1))
                    instance_index = int(m.group(2))
                    break
        is_new = True

    # "한/글 떠 있었지만 attach 불가" 판정 (IL 분리 또는 외부 한/글 ROT 미등록)
    pre_existing = bool(had_hwp_before and is_new)

    # 한컴 보안 승인 모듈 — 자동화 API 의 파일 접근 다이얼로그 차단.
    # 이 PC 에 'FilePathCheckerModule' 이름으로 등록됨 (한컴 예시 이름과 다름).
    try:
        hwp.RegisterModule("FilePathCheckDLL", "FilePathCheckerModule")
    except Exception:
        pass

    # Visible=True 는 신규 spawn (is_new=True) 시에만 호출.
    # ─ 한컴 공식 API 카탈로그 (Visible Property id=308 / 한글오토메이션EventHandler추가
    #   가이드 id=362) 확인: CreateDispatch 직후 한정 호출 예시. 신규 COM 인스턴스는
    #   hidden default 라 명시적으로 보여줘야 함.
    # ─ ROT attach 경로 (is_new=False) 는 사용자가 이미 띄운 윈도우라 재호출 불필요.
    #   오히려 Visible setter 가 호출되면 한/글이 윈도우를 자동화 default geometry 로
    #   reshow → 사용자가 키워둔 윈도우 크기가 reset 되는 부작용 (tool2 와 동일 현상).
    if visible and is_new:
        try:
            hwp.XHwpWindows.Item(0).Visible = True
        except Exception:
            pass

    return HwpSession(
        hwp=hwp,
        is_new=is_new,
        pre_existing=pre_existing,
        version_code=version_code,
        instance_index=instance_index,
        moniker_name=moniker_name,
    )


def detach(session: HwpSession, quit_if_new: bool = False) -> None:
    """
    세션 정리. quit_if_new=True 면 우리가 띄운 경우만 한/글 종료.
    기본 False — 사용자 작업 보존 우선.
    """
    if quit_if_new and session.is_new:
        try:
            session.hwp.Quit()
        except Exception:
            pass
