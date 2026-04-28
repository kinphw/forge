"""
간단한 hover 툴팁 — Tk 기본에 없는 기능을 짧게 구현.

사용:
    Tooltip(button, "이 버튼은 …")

위젯에 <Enter>/<Leave>/<ButtonPress> 바인딩만 추가. 추가 의존성 없음.
delay ms 후 Toplevel 윈도우 (decoration 제거) 로 라벨 1개 띄우고,
마우스 이탈/클릭 시 즉시 destroy.
"""
from __future__ import annotations

import tkinter as tk
from typing import Optional


class Tooltip:
    """위젯에 hover 툴팁 부착.

    delay 동안 hover 유지 시 위젯 좌하단에 yellow 박스로 text 표시.
    동일 위젯에 여러 번 attach 해도 add=True 로 충돌 없음.
    `update_text()` 로 내용 갱신 가능 (예: 단축키 letter 변경 시).
    """

    def __init__(
        self,
        widget: tk.Misc,
        text: str,
        delay: int = 450,
        wraplength: int = 360,
    ):
        self.widget = widget
        self.text = text
        self.delay = delay
        self.wraplength = wraplength
        self._tip: Optional[tk.Toplevel] = None
        self._after_id: Optional[str] = None
        widget.bind("<Enter>", self._on_enter, add=True)
        widget.bind("<Leave>", self._on_leave, add=True)
        widget.bind("<ButtonPress>", self._on_leave, add=True)

    # ---------------------- internal
    def _on_enter(self, _evt=None) -> None:
        self._cancel_after()
        try:
            self._after_id = self.widget.after(self.delay, self._show)
        except Exception:
            pass

    def _on_leave(self, _evt=None) -> None:
        self._cancel_after()
        self._hide()

    def _cancel_after(self) -> None:
        if self._after_id is not None:
            try:
                self.widget.after_cancel(self._after_id)
            except Exception:
                pass
            self._after_id = None

    def _show(self) -> None:
        if self._tip is not None:
            return
        if not self.text:
            return
        try:
            x = self.widget.winfo_rootx() + 12
            y = self.widget.winfo_rooty() + self.widget.winfo_height() + 4
        except Exception:
            return
        tip = tk.Toplevel(self.widget)
        tip.wm_overrideredirect(True)  # 윈도우 데코레이션 제거
        tip.wm_geometry(f"+{x}+{y}")
        # Win11 dark/light 모두에서 가독성 유지하도록 색상 명시
        tk.Label(
            tip, text=self.text,
            background="#ffffe0", foreground="#222",
            relief="solid", borderwidth=1,
            font=("", 9), padx=8, pady=4,
            justify="left", wraplength=self.wraplength,
        ).pack()
        self._tip = tip

    def _hide(self) -> None:
        if self._tip is not None:
            try:
                self._tip.destroy()
            except Exception:
                pass
            self._tip = None

    # ---------------------- public
    def update_text(self, text: str) -> None:
        """툴팁 내용 갱신. 현재 표시 중이면 다음 hover 부터 새 내용."""
        self.text = text
        # 현재 떠 있는 팁이 있으면 destroy — 다음 hover 시 새 텍스트로 재생성
        self._hide()
