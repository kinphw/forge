"""
Win32 시스템 전역 hotkey 매니저.

Tkinter 의 `bind_all` 은 Forge 윈도우가 포커스를 가진 동안만 작동. 사용자가
한/글에서 작업하면서 Forge 룰을 hotkey 로 호출하려면 OS 전역 hotkey 가 필요.

Win32 `RegisterHotKey` + `GetMessage` 메시지 펌프를 별도 스레드에서 돌리고,
WM_HOTKEY 수신 시 Tk root.after(0, callback) 로 UI 스레드에 dispatch.

★ 동시 등록 가능 hotkey 수: Win32 제한 없으나 동일 조합을 다른 프로세스가
이미 잡고 있으면 RegisterHotKey 가 실패 → log 만 남기고 다른 hotkey 는 정상
동작. 사용자가 충돌 모르고 "안 됨" 으로 보지 않도록 GlobalHotkeyManager.start
는 등록 결과 리스트를 반환.
"""
from __future__ import annotations

import ctypes
import threading
from ctypes import wintypes
from dataclasses import dataclass
from queue import Queue, Empty
from typing import Any, Callable, Optional

# Win32 modifier bits
MOD_ALT     = 0x0001
MOD_CONTROL = 0x0002
MOD_SHIFT   = 0x0004
MOD_WIN     = 0x0008
MOD_NOREPEAT = 0x4000  # Windows 7+ — 키 누르고 있어도 한 번만 발화

WM_HOTKEY = 0x0312
WM_QUIT   = 0x0012
# 사용자 정의 메시지 — 펌프 스레드에 명령 큐 처리 요청
# WM_APP (0x8000) 부터는 application-defined 영역 (Microsoft 공식)
WM_APP_RELOAD = 0x8000 + 1


@dataclass
class _HotkeyDef:
    hk_id: int
    modifiers: int
    vk: Optional[int]   # Virtual-Key code (None = 비활성). 단순 ASCII 키는 ord('Q')
    callback: Callable[[], Any]
    label: str          # 사용자/로그용 라벨 (예: "Ctrl+Shift+Q")
    registered: bool = False  # 현재 RegisterHotKey 성공 상태


class GlobalHotkeyManager:
    """
    Win32 RegisterHotKey 기반 전역 hotkey 매니저.

    사용법:
        mgr = GlobalHotkeyManager(root)
        mgr.add(1, MOD_CONTROL | MOD_SHIFT, ord('Q'), callback_q, "Ctrl+Shift+Q")
        results = mgr.start()
        # results: [(label, ok), ...] — 등록 실패한 항목 식별 가능

        # 종료 시
        mgr.stop()

    각 hk_id 는 프로세스 내 unique 1~0xBFFF.
    """

    def __init__(self, root: Any):
        self.root = root
        self._defs: list[_HotkeyDef] = []
        self._thread: Optional[threading.Thread] = None
        self._thread_id: int = 0
        self._stop_event = threading.Event()
        # 동적 변경 명령 큐 — UI 스레드 → 펌프 스레드 (replace 등)
        # 항목: ('replace', hk_id, new_vk, new_label, result_list, done_event)
        self._cmd_queue: Queue = Queue()
        # Win32 user32 — ctypes 로 직접 호출 (pywin32 의 win32gui 는 모듈 의존성
        # 사슬이 길어 ctypes 가 더 가벼움)
        self._user32 = ctypes.windll.user32
        # 시그니처 명시 — 64bit 환경에서 wParam/lParam 등 폭 보장
        self._user32.RegisterHotKey.restype = wintypes.BOOL
        self._user32.UnregisterHotKey.restype = wintypes.BOOL
        self._user32.GetMessageW.restype = ctypes.c_int
        self._user32.PostThreadMessageW.restype = wintypes.BOOL

    def add(
        self,
        hk_id: int,
        modifiers: int,
        vk: Optional[int],
        callback: Callable[[], Any],
        label: str = "",
    ) -> None:
        """단축키 정의 추가. start() 전에 호출.

        vk=None 이면 비활성화 상태로 등록 — _run_loop 가 RegisterHotKey skip.
        사용자가 settings 에서 명시적으로 비활성화한 단축키 표현용.
        """
        self._defs.append(_HotkeyDef(hk_id, modifiers, vk, callback, label))

    def start(self) -> list[tuple[str, bool]]:
        """
        백그라운드 스레드 시작. 등록 결과를 리스트로 반환.
        반환: [(label, ok), ...]  — ok=False 면 다른 앱이 이미 잡고 있는 조합.
        """
        # 결과 수집을 위한 동기화
        self._register_done = threading.Event()
        self._register_results: list[tuple[str, bool]] = []
        self._thread = threading.Thread(target=self._run_loop, daemon=True)
        self._thread.start()
        # 등록 완료까지 대기 (최대 2초)
        self._register_done.wait(timeout=2.0)
        return self._register_results

    def stop(self) -> None:
        """매니저 종료 — 등록된 hotkey 모두 해제 + 메시지 펌프 종료."""
        if self._thread is None or not self._thread.is_alive():
            return
        self._stop_event.set()
        # PostThreadMessage(WM_QUIT) 로 GetMessage 풀어 줌
        try:
            self._user32.PostThreadMessageW(self._thread_id, WM_QUIT, 0, 0)
        except Exception:
            pass
        self._thread.join(timeout=1.0)

    def replace(
        self, hk_id: int, new_vk: Optional[int], new_label: str,
        timeout: float = 2.0,
    ) -> bool:
        """
        기존 hotkey 의 vk 변경 또는 비활성화. UI 스레드 안전 (펌프 스레드에 위임).

        Args:
            hk_id: add() 시 부여한 식별자.
            new_vk: 새 Virtual-Key code. None 이면 비활성화 (등록 해제만).
            new_label: 새 표시 라벨.
            timeout: 펌프 스레드 응답 대기 (초).

        Returns:
            True: 등록 성공 (또는 비활성화 성공).
            False: RegisterHotKey 실패 (다른 앱·내부 중복) — 매니저는 OLD 상태로 자동
                복원하므로 변경 전 hotkey 가 그대로 살아있음.

        ★ thread-affinity: RegisterHotKey 는 호출 스레드에 hotkey 메시지를 묶음.
          그래서 UI 스레드에서 직접 호출하면 메시지가 펌프 스레드로 안 옴.
          명령을 큐에 넣고 PostThreadMessage 로 펌프를 깨워 처리하게 함.
        """
        if self._thread is None or not self._thread.is_alive():
            return False
        result: list[bool] = [False]
        done = threading.Event()
        self._cmd_queue.put(("replace", hk_id, new_vk, new_label, result, done))
        try:
            self._user32.PostThreadMessageW(self._thread_id, WM_APP_RELOAD, 0, 0)
        except Exception:
            return False
        if not done.wait(timeout=timeout):
            return False
        return result[0]

    def get_status(self, hk_id: int) -> Optional[bool]:
        """
        hk_id 의 현재 등록 상태 — True (등록됨) / False (등록 실패) / None (미정의 hk_id).
        """
        for d in self._defs:
            if d.hk_id == hk_id:
                return d.registered
        return None

    # ----------------------------------------------------- internal
    def _run_loop(self) -> None:
        # 현재 스레드 ID 기록 — stop()/replace() 에서 PostThreadMessage 용
        self._thread_id = ctypes.windll.kernel32.GetCurrentThreadId()

        # Hotkey 일괄 등록 — vk=None 인 항목은 skip (비활성화 상태)
        results: list[tuple[str, bool]] = []
        for d in self._defs:
            if d.vk is None:
                d.registered = False
                results.append((d.label, True))  # 비활성화는 "원하는 대로 됨"
                continue
            ok = bool(self._user32.RegisterHotKey(
                None, d.hk_id, d.modifiers | MOD_NOREPEAT, d.vk,
            ))
            d.registered = ok
            results.append((d.label, ok))
        self._register_results = results
        self._register_done.set()

        # 메시지 펌프
        msg = wintypes.MSG()
        try:
            while not self._stop_event.is_set():
                # GetMessage 는 메시지 올 때까지 block. WM_QUIT 받으면 0 반환.
                ret = self._user32.GetMessageW(ctypes.byref(msg), None, 0, 0)
                if ret == 0 or ret == -1:
                    break  # WM_QUIT or error
                if msg.message == WM_HOTKEY:
                    hk_id = int(msg.wParam)
                    for d in self._defs:
                        if d.hk_id == hk_id and d.registered:
                            self._dispatch(d.callback)
                            break
                elif msg.message == WM_APP_RELOAD:
                    # UI 스레드에서 들어온 명령 큐 비우기
                    self._drain_cmd_queue()
        finally:
            # 정리 — 현재 registered=True 인 모든 hotkey 해제
            for d in self._defs:
                if d.registered:
                    try:
                        self._user32.UnregisterHotKey(None, d.hk_id)
                    except Exception:
                        pass
                    d.registered = False

    def _drain_cmd_queue(self) -> None:
        """UI 스레드가 큐에 넣은 명령들을 펌프 스레드에서 처리."""
        while True:
            try:
                cmd = self._cmd_queue.get_nowait()
            except Empty:
                return
            kind = cmd[0]
            if kind == "replace":
                _, hk_id, new_vk, new_label, result, done = cmd
                ok = self._do_replace(hk_id, new_vk, new_label)
                result[0] = ok
                done.set()

    def _do_replace(
        self, hk_id: int, new_vk: Optional[int], new_label: str,
    ) -> bool:
        """
        펌프 스레드 안에서 실제 RegisterHotKey/UnregisterHotKey 수행.

        - new_vk=None: 비활성화 — Unregister 만, registered=False
        - new_vk=int : Unregister(old) → Register(new). 실패 시 OLD 복원.
        """
        d = next((x for x in self._defs if x.hk_id == hk_id), None)
        if d is None:
            return False

        old_vk = d.vk
        old_registered = d.registered

        # 기존 등록 해제
        if old_registered:
            try:
                self._user32.UnregisterHotKey(None, d.hk_id)
            except Exception:
                pass
            d.registered = False

        # 비활성화 요청
        if new_vk is None:
            d.vk = None
            d.label = new_label
            return True

        # 신규 등록 시도
        ok = bool(self._user32.RegisterHotKey(
            None, d.hk_id, d.modifiers | MOD_NOREPEAT, new_vk,
        ))
        if ok:
            d.vk = new_vk
            d.label = new_label
            d.registered = True
            return True

        # 실패 — OLD 복원 시도
        if old_registered and old_vk is not None:
            restored = bool(self._user32.RegisterHotKey(
                None, d.hk_id, d.modifiers | MOD_NOREPEAT, old_vk,
            ))
            d.registered = restored
        return False

    def _dispatch(self, callback: Callable[[], Any]) -> None:
        """callback 을 Tk UI 스레드로 안전 전달."""
        try:
            self.root.after(0, callback)
        except Exception:
            # root 이 이미 destroy 됐거나 비정상 상태 — 그냥 무시
            pass


# 자주 쓰는 키 코드 (Virtual-Key) — A~Z 는 ord(대문자) 와 동일
def vk(letter: str) -> int:
    """알파벳 1글자 → VK 코드. 'q' → ord('Q')."""
    return ord(letter.upper())
