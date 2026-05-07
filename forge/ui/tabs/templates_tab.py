"""
탭 ④ 양식삽입 — 고정된 보고서 양식을 활성 한/글 문서 커서 위치에 삽입.

각 버튼 = 양식 1 종 emit. 그룹 변경 지점마다 ttk.Separator 삽입 (개별 작업
탭과 동일 패턴). 이미지 인자는 현재 카탈로그에 없음.
"""
from __future__ import annotations

import threading
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from tkinter.constants import LEFT, RIGHT, BOTH, X, Y, W
from tkinter.scrolledtext import ScrolledText
from tkinter.ttk import LabelFrame as TtkLabelFrame
from typing import TYPE_CHECKING, Callable, Optional

from forge.hwp_session import (
    MultipleHwpInstancesError,
    NoExistingHwpError,
    init_com_for_thread,
)
from forge.templates import 금감_TEMPLATES

from ..tooltip import Tooltip

if TYPE_CHECKING:
    from ..app import ForgeApp


class TemplatesTab:
    def __init__(self, parent: tk.Misc, app: "ForgeApp"):
        self.app = app
        self.frame = ttk.Frame(parent, padding=12)

        # ─── 헤더 ───
        title = ttk.Label(
            self.frame,
            text="양식삽입",
            font=("", 14, "bold"),
        )
        title.pack(anchor=W, pady=(0, 4))

        desc = ttk.Label(
            self.frame,
            text="고정된 보고서 양식을 삽입합니다. 버튼 클릭 시 활성 한/글 문서 현재 커서 위치에 emit.",
            justify="left", foreground="#555",
        )
        desc.pack(anchor=W, pady=(0, 10))

        # ─── 단일 grid + 그룹 변경 지점에 ttk.Separator (실시간 탭과 동일 패턴) ───
        body = TtkLabelFrame(
            self.frame,
            text="🧪 활성 한/글 문서의 현재 커서 위치에 삽입됨",
            padding=10,
        )
        body.pack(fill=X, pady=(0, 6))

        grid = ttk.Frame(body)
        grid.pack(fill=X, anchor="w")
        # 컬럼 0: 버튼, 컬럼 1: 설명
        grid.columnconfigure(0, minsize=240)
        grid.columnconfigure(1)

        prev_group: Optional[str] = None
        row_idx = 0
        for num, group, fn, label, desc_text, img_count in 금감_TEMPLATES:
            # 그룹 변경 지점에 separator
            if prev_group is not None and group != prev_group:
                ttk.Separator(grid, orient="horizontal").grid(
                    row=row_idx, column=0, columnspan=2,
                    sticky="ew", pady=(6, 6),
                )
                row_idx += 1
            prev_group = group

            btn = ttk.Button(
                grid, text=f"[{num}] {label}",
                width=28,
                command=lambda f=fn, n=num, ic=img_count, lb=label:
                    self._on_click(f, n, ic, lb),
            )
            btn.grid(row=row_idx, column=0, sticky="w", pady=2, padx=(0, 8))
            Tooltip(btn, f"{label} — {desc_text}")
            ttk.Label(
                grid, text=desc_text, foreground="#666",
            ).grid(row=row_idx, column=1, sticky="w")
            row_idx += 1

        # ─── 디버그 로그 창 ───
        log_frame = TtkLabelFrame(self.frame, text="📋 로그", padding=4)
        log_frame.pack(fill=X, pady=(4, 0))
        self.log_text = ScrolledText(
            log_frame, height=6, wrap="none",
            font=("Consolas", 9),
        )
        self.log_text.pack(fill=X)

    # ──────────────────────────────────────── 클릭 핸들러
    def _on_click(self, fn: Callable, num: int, img_count: int, label: str) -> None:
        """버튼 클릭 — 이미지 prompt 후 백그라운드 실행."""
        images: list[Optional[str]] = []
        for i in range(img_count):
            path = filedialog.askopenfilename(
                parent=self.frame.winfo_toplevel(),
                title=f"[{num}] {label} — 이미지 {i + 1}/{img_count} 선택 (취소 시 skip)",
                filetypes=[("이미지", "*.png *.jpg *.jpeg *.bmp *.gif"),
                            ("모두", "*.*")],
            )
            images.append(path or None)

        threading.Thread(
            target=self._run_worker,
            args=(fn, num, label, images),
            daemon=True,
        ).start()

    def _run_worker(
        self, fn: Callable, num: int, label: str,
        images: list[Optional[str]],
    ) -> None:
        from datetime import datetime
        ts = datetime.now().strftime("%H:%M:%S")
        self._log(f"[{ts}] [{num}] {label} 시작")
        init_com_for_thread()
        try:
            try:
                session = self.app.ensure_hwp()
            except MultipleHwpInstancesError as e:
                self._log(f"  ⚠ 한/글 인스턴스 {len(e.instances)}개 — picker")
                self.app.prompt_pick_from_worker(e.instances)
                return
            except NoExistingHwpError as e:
                self._log(f"  ✘ 한/글 미실행: {e}")
                self.frame.after(0, lambda: messagebox.showwarning(
                    "한/글 미실행",
                    "떠 있는 한/글 인스턴스가 없습니다.\n한/글을 먼저 실행해 주세요.",
                ))
                return
            hwp = session.hwp
            try:
                if images:
                    fn(hwp, *images)
                else:
                    fn(hwp)
                self._log(f"  ✔ [{num}] {label} 완료")
                self.app._set_status(f"✔ [{num}] {label} 완료")
            except Exception as e:
                import traceback
                self._log(f"  ✘ [{num}] {label} 실패: {type(e).__name__}: {e}")
                self._log("  " + traceback.format_exc().replace("\n", "\n  "))
                self.app._set_status(f"✘ [{num}] {label} 실패: {e}")
        except Exception as e:
            self._log(f"  ✘ 예기치 못한 오류: {e}")

    # ──────────────────────────────────────── 로그 헬퍼
    def _log(self, msg: str) -> None:
        def append():
            try:
                inner = getattr(self.log_text, "text", self.log_text)
                inner.insert("end", msg + "\n")
                inner.see("end")
            except Exception:
                pass
        try:
            self.frame.after(0, append)
        except Exception:
            pass

    def on_hwp_ready(self) -> None:
        pass
