"""
한/글 인스턴스 선택 다이얼로그.

여러 한/글 프로세스가 떠 있을 때 사용자가 의도한 인스턴스를 명시적으로
고르도록 강제 (silent first-match 회피). 각 인스턴스의 활성 문서 파일명을
함께 보여줘 식별 용이.

사용:
    chosen = pick_hwp_instance(parent_window, instances, current_moniker)
    if chosen is not None:
        # 사용자가 골랐음
"""
from __future__ import annotations

import tkinter as tk
from tkinter import ttk
from tkinter.constants import LEFT, RIGHT, BOTH, X, W
from typing import Optional

from forge.hwp_session import HwpInstance


class HwpPickerDialog:
    """모달 인스턴스 선택 다이얼로그. 결과는 self.result 에 (None=취소)."""

    def __init__(
        self,
        parent: tk.Misc,
        instances: list[HwpInstance],
        current_moniker: Optional[str] = None,
    ):
        self.instances = instances
        self.result: Optional[HwpInstance] = None

        self.top = tk.Toplevel(parent)
        self.top.title("한/글 인스턴스 선택")
        self.top.transient(parent)
        self.top.grab_set()
        self.top.resizable(True, False)

        # 부모 중앙 정렬 — 부모 윈도우 좌표 기준
        try:
            parent.update_idletasks()
            px = parent.winfo_rootx()
            py = parent.winfo_rooty()
            pw = parent.winfo_width()
            ph = parent.winfo_height()
            ww, wh = 560, 320
            x = px + (pw - ww) // 2
            y = py + (ph - wh) // 3
            self.top.geometry(f"{ww}x{wh}+{max(x,0)}+{max(y,0)}")
        except Exception:
            self.top.geometry("560x320")

        body = ttk.Frame(self.top, padding=12)
        body.pack(fill=BOTH, expand=True)

        ttk.Label(
            body,
            text="현재 시스템에 떠 있는 한/글 인스턴스 목록입니다.\n"
                 "작업 대상 인스턴스를 선택해 주세요.",
            justify="left",
        ).pack(anchor=W, pady=(0, 8))

        # 리스트 + 스크롤바
        list_frame = ttk.Frame(body)
        list_frame.pack(fill=BOTH, expand=True)

        self.listbox = tk.Listbox(
            list_frame, height=8, font=("Segoe UI", 10),
            selectmode=tk.SINGLE, exportselection=False,
        )
        sb = ttk.Scrollbar(list_frame, orient="vertical", command=self.listbox.yview)
        self.listbox.config(yscrollcommand=sb.set)
        self.listbox.pack(side=LEFT, fill=BOTH, expand=True)
        sb.pack(side=RIGHT, fill="y")

        # 채우기 + 현재 선택된 항목에 표시
        preselect_idx = 0
        for i, inst in enumerate(instances):
            mark = "● " if inst.moniker_name == current_moniker else "  "
            self.listbox.insert("end", f"{mark}{inst.display_label}")
            if inst.moniker_name == current_moniker:
                preselect_idx = i
        if instances:
            self.listbox.selection_set(preselect_idx)
            self.listbox.activate(preselect_idx)
            self.listbox.see(preselect_idx)

        # 더블클릭 = 확인
        self.listbox.bind("<Double-Button-1>", lambda e: self._on_ok())

        # 하단 정보 라벨 (선택된 인스턴스의 전체 경로)
        self.detail_var = tk.StringVar()
        ttk.Label(
            body, textvariable=self.detail_var, foreground="#555",
            font=("Segoe UI", 9),
        ).pack(anchor=W, pady=(6, 0))
        self.listbox.bind("<<ListboxSelect>>", self._on_select)
        self._on_select()  # 초기 표시

        # 버튼 행
        btns = ttk.Frame(body)
        btns.pack(fill=X, pady=(10, 0))
        ttk.Button(btns, text="취소", command=self._on_cancel, width=10).pack(side=RIGHT)
        ttk.Button(btns, text="선택", command=self._on_ok, width=10).pack(
            side=RIGHT, padx=(0, 6))
        ttk.Button(btns, text="🔄 새로고침", command=self._refresh, width=12).pack(side=LEFT)

        # ESC = 취소, Enter = 확인
        self.top.bind("<Escape>", lambda e: self._on_cancel())
        self.top.bind("<Return>", lambda e: self._on_ok())
        self.top.protocol("WM_DELETE_WINDOW", self._on_cancel)

        self.listbox.focus_set()

    def _on_select(self, _evt=None) -> None:
        sel = self.listbox.curselection()
        if not sel:
            self.detail_var.set("")
            return
        inst = self.instances[sel[0]]
        path = inst.active_file_path or "(저장되지 않은 새 문서)"
        self.detail_var.set(f"경로: {path}")

    def _refresh(self) -> None:
        """ROT 재스캔 — 사용자가 한/글을 새로 띄우거나 닫은 경우."""
        from forge.hwp_session import list_hwp_instances
        new_list = list_hwp_instances()
        self.instances = new_list
        self.listbox.delete(0, "end")
        # 현재 선택된 moniker (혹은 새로고침 전 첫 항목) 유지 시도
        keep_moniker = None
        try:
            sel = self.listbox.curselection()
            if sel and 0 <= sel[0] < len(new_list):
                keep_moniker = new_list[sel[0]].moniker_name
        except Exception:
            pass
        for i, inst in enumerate(new_list):
            mark = "● " if inst.moniker_name == keep_moniker else "  "
            self.listbox.insert("end", f"{mark}{inst.display_label}")
        if new_list:
            self.listbox.selection_set(0)
            self.listbox.activate(0)
        self._on_select()

    def _on_ok(self) -> None:
        sel = self.listbox.curselection()
        if not sel or not self.instances:
            return
        self.result = self.instances[sel[0]]
        self.top.destroy()

    def _on_cancel(self) -> None:
        self.result = None
        self.top.destroy()


def pick_hwp_instance(
    parent: tk.Misc,
    instances: list[HwpInstance],
    current_moniker: Optional[str] = None,
) -> Optional[HwpInstance]:
    """모달 다이얼로그 띄워 사용자 선택 받기. 취소/닫기 시 None."""
    dlg = HwpPickerDialog(parent, instances, current_moniker)
    parent.wait_window(dlg.top)
    return dlg.result
