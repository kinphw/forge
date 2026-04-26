"""
한/글 COM 인스턴스 attach·신규 생성 헬퍼.

Forge GUI는 실행 시 시스템에 이미 떠 있는 한/글 프로세스가 있으면
거기에 붙고, 없으면 새 인스턴스를 띄운다 (보이는 상태로). 사용자는
어느 경우든 같은 한/글 창에서 작업 가능.

연결 정책:
  - 이미 EnsureDispatch 로 잡힌 인스턴스가 있으면 재사용 (gen_py 캐시)
  - tool2 / tool1 / 사용자 손작업으로 띄운 한/글이 있으면 그 인스턴스에 attach
  - 아무것도 없으면 새 인스턴스 생성 후 visible=True
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Optional


@dataclass
class HwpSession:
    """한/글 COM 인스턴스 wrapper."""

    hwp: Any                          # win32com.client COM 객체 (HwpFrame.HwpObject)
    is_new: bool                      # 우리가 새로 띄웠으면 True, 기존에 attach 했으면 False
    process_pid: Optional[int] = None # 한/글 프로세스 PID (가능하면)


def init_com_for_thread() -> None:
    """
    COM 사용 전 현재 스레드에 대해 CoInitialize 호출.

    각 백그라운드 스레드가 COM 객체를 만들기 전 1회 호출해야 함.
    이미 init 된 스레드에서 다시 호출해도 안전 (S_FALSE 반환).
    스레드가 종료되면 자동 cleanup 됨 (별도 CoUninitialize 불필요).

    이 호출이 누락되면 "CoInitialize가 호출되지 않았습니다"
    (HRESULT 0x800401F0) 오류 발생.
    """
    import pythoncom
    pythoncom.CoInitialize()


def attach_or_create(visible: bool = True) -> HwpSession:
    """
    한/글 COM 에 연결한다.

    한/글은 IRunningObjectTable 정책상 `Dispatch`/`EnsureDispatch` 만으로는
    기존 GUI 프로세스에 안 붙고 별도 COM 인스턴스(=별도 한/글 프로세스)를
    spawn 하는 경우가 흔하다. 따라서 `GetActiveObject` 로 ROT 직접 조회를
    우선 시도하고, 실패 시에만 EnsureDispatch 로 신규 생성한다.

    호출 스레드에 대해 CoInitialize 자동 수행. 백그라운드 스레드에서
    호출되어도 안전.
    """
    init_com_for_thread()
    from win32com.client import gencache, GetActiveObject

    is_new = False
    try:
        # ROT 에서 이미 떠 있는 한/글 인스턴스 우선 attach.
        hwp = GetActiveObject("HwpFrame.HwpObject")
    except Exception:
        # ROT 에 등록된 한/글 없음 — 신규 생성
        hwp = gencache.EnsureDispatch("HwpFrame.HwpObject")
        is_new = True

    # 한컴 보안 승인 모듈 등록 — 자동화 API 의 파일 접근 다이얼로그 차단.
    # 사용자 PC 의 레지스트리에 'FilePathCheckerModule' 이름으로 등록된
    # FilePathCheckerModuleExample.dll 을 활용. 등록 안 된 환경에서는
    # 호출이 실패해도 무시 (다이얼로그가 뜰 뿐 동작엔 영향 없음).
    try:
        hwp.RegisterModule("FilePathCheckDLL", "FilePathCheckerModule")
    except Exception:
        pass

    if visible:
        try:
            hwp.XHwpWindows.Item(0).Visible = True
        except Exception:
            pass

    return HwpSession(hwp=hwp, is_new=is_new)


def detach(session: HwpSession, quit_if_new: bool = False) -> None:
    """
    세션 정리. quit_if_new=True 면 우리가 띄운 경우만 한/글을 종료.
    기본값은 False — 사용자 작업 보존 우선.
    """
    if quit_if_new and session.is_new:
        try:
            session.hwp.Quit()
        except Exception:
            pass
