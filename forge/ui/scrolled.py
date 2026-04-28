"""
Pure-tkinter ScrolledFrame — ttkbootstrap.scrolled.ScrolledFrame 대체.

Canvas + 내부 Frame + Scrollbar 조합. 자식은 `.interior` 에 배치한다.
사용법:
    sf = ScrolledFrame(parent, padding=12)
    sf.pack(fill="both", expand=True)
    ttk.Label(sf.interior, text="...").pack()
"""
from __future__ import annotations

import tkinter as tk
from tkinter import ttk


class ScrolledFrame(ttk.Frame):
    def __init__(self, master, padding: int = 0, **kwargs):
        super().__init__(master, **kwargs)
        self.canvas = tk.Canvas(self, borderwidth=0, highlightthickness=0)
        self.vsb = ttk.Scrollbar(self, orient="vertical", command=self.canvas.yview)
        self.canvas.configure(yscrollcommand=self.vsb.set)
        self.vsb.pack(side="right", fill="y")
        self.canvas.pack(side="left", fill="both", expand=True)

        self.interior = ttk.Frame(self.canvas, padding=padding)
        self._win_id = self.canvas.create_window(
            (0, 0), window=self.interior, anchor="nw"
        )

        self.interior.bind("<Configure>", self._on_interior_configure)
        self.canvas.bind("<Configure>", self._on_canvas_configure)

        # 휠 스크롤 — 위젯 위에 마우스 있을 때만 활성화 (전역 bind 회피)
        self.canvas.bind("<Enter>", self._bind_wheel)
        self.canvas.bind("<Leave>", self._unbind_wheel)
        self.interior.bind("<Enter>", self._bind_wheel)
        self.interior.bind("<Leave>", self._unbind_wheel)

    def _on_interior_configure(self, _event) -> None:
        self.canvas.configure(scrollregion=self.canvas.bbox("all"))

    def _on_canvas_configure(self, event) -> None:
        self.canvas.itemconfigure(self._win_id, width=event.width)

    def _bind_wheel(self, _event) -> None:
        self.canvas.bind_all("<MouseWheel>", self._on_mousewheel)

    def _unbind_wheel(self, _event) -> None:
        self.canvas.unbind_all("<MouseWheel>")

    def _on_mousewheel(self, event) -> None:
        self.canvas.yview_scroll(int(-event.delta / 120), "units")
