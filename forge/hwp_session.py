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
  4. 없으면: gencache.EnsureDispatch("HWPFrame.HwpObject") 로 신규 spawn
     (ProgID 가 버전-무관 alias 라 OS 기본 등록 버전이 spawn 됨)

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


def _find_in_rot() -> Optional[tuple[Any, str, int, int]]:
    """
    ROT 에서 살아있는 HWPFrame.HwpObject 인스턴스 찾기.

    동일 integrity level 의 한/글만 enum 가능. 발견하면 IDispatch 추출 후
    gencache.EnsureDispatch(disp) 로 early-bound wrapper 만들어 반환.

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
        version_code = int(m.group(1))
        instance_index = int(m.group(2))
        try:
            obj = rot.GetObject(mk)
            disp = obj.QueryInterface(pythoncom.IID_IDispatch)
            hwp = gencache.EnsureDispatch(disp)
        except Exception:
            continue
        # dead moniker 가 ROT 에 잔존할 수 있음 — 가벼운 호출로 검증
        if is_alive(hwp):
            return hwp, name, version_code, instance_index
    return None


def attach_or_create(visible: bool = True) -> HwpSession:
    """
    한/글 COM 연결 — ROT attach 우선, 없으면 신규 spawn.

    호출 스레드에 대해 CoInitialize 자동 수행. 한/글 버전 (80=2010 ~
    130=2024) 은 모두 동일 패턴으로 매칭되므로 어떤 버전이 깔린 환경에서도
    추가 코드 변경 없이 작동.
    """
    init_com_for_thread()

    # 한/글이 시스템에 떠 있는지 사전 확인 (pre_existing 판정용)
    had_hwp_before = is_hwp_running()

    # 1. ROT enum + GetObject 로 기존 인스턴스 attach 시도
    found = _find_in_rot()
    is_new = False
    if found is not None:
        hwp, moniker_name, version_code, instance_index = found
    else:
        # 2. 신규 spawn — gencache.EnsureDispatch 가 자동으로 ROT 등록함.
        #    PyIDispatch 동등성 비교는 신뢰 못해서, spawn 전후 ROT moniker 의
        #    차집합으로 우리가 방금 등록한 인스턴스 식별.
        before_monikers = _list_rot_monikers()
        from win32com.client import gencache
        hwp = gencache.EnsureDispatch("HWPFrame.HwpObject")
        is_new = True
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

    # "한/글 떠 있었지만 attach 불가" 판정 (IL 분리 또는 외부 한/글 ROT 미등록)
    pre_existing = bool(had_hwp_before and is_new)

    # 한컴 보안 승인 모듈 — 자동화 API 의 파일 접근 다이얼로그 차단.
    # 이 PC 에 'FilePathCheckerModule' 이름으로 등록됨 (한컴 예시 이름과 다름).
    try:
        hwp.RegisterModule("FilePathCheckDLL", "FilePathCheckerModule")
    except Exception:
        pass

    if visible:
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
