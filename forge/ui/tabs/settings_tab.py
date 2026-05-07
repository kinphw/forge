"""
탭 ① 기본정보 — 보고서 양식 spec 표시 + 사용자 테일러링.

★ 보고서명·작성부서·작성일 같은 문서 메타데이터는 여기 없음.
  그건 마크다운 front-matter 의 영역 (탭 ②).

여기서 다루는 것은 '구조 양식' 만:
  - 보고서 템플릿 선택 (금감원페이지 외)
  - 페이지 여백 / 줄간격

★ 폰트·크기 / 본문 글머리 / 주석 spec 은 의도적으로 여기에 없음.
  realtime_tab (개별 작업) 의 4 폰트 cluster + var_blank_size 가 SSOT —
  변환 시점에 자동 주입됨. 한 곳에서만 폰트를 만진다.

기본값은 forge.formatter.templates.REPORT1_SPEC (= 금감원페이지 표준).
사용자가 필드를 수정하면 self.state.spec 에 반영되어 탭 ② 변환에 사용됨.
"""
from __future__ import annotations

import tkinter as tk
from tkinter import ttk
from tkinter.constants import LEFT, RIGHT, BOTH, X, W
from tkinter.ttk import LabelFrame as TtkLabelFrame
from typing import TYPE_CHECKING

from forge.formatter.templates import REPORT1_SPEC
from ..scrolled import ScrolledFrame

if TYPE_CHECKING:
    from ..app import AppState


class SettingsTab:
    def __init__(self, parent: tk.Misc, state: "AppState"):
        self.state = state
        # 외부 frame (Notebook 에 add 되는 것)
        self.frame = ttk.Frame(parent)
        # 내부에 ScrolledFrame — 탭 콘텐츠가 길어서 스크롤 필요
        scrolled = ScrolledFrame(self.frame, padding=12)
        scrolled.pack(fill=BOTH, expand=True)
        # 자식들은 scrolled.interior 에 추가 (Canvas 안 내부 frame)
        content = scrolled.interior

        # ─── 안내 ───
        ttk.Label(
            content,
            text="※ 폰트·크기·글머리·주석 spec 은 '개별 작업' 탭의 4 폰트 cluster + 빈줄 크기가 SSOT 입니다.\n"
                 "   여기서는 구조 양식(여백·줄간격)만 다룹니다.",
            foreground="#555",
            justify="left",
        ).pack(anchor=W, pady=(0, 12))

        # ─── 보고서 템플릿 선택 ───
        tmpl = TtkLabelFrame(content, text="보고서 템플릿", padding=10)
        tmpl.pack(fill=X, pady=(0, 10))
        ttk.Label(tmpl, text="템플릿:").pack(side=LEFT)
        self.var_template = tk.StringVar(value=REPORT1_SPEC.name)
        templates = [REPORT1_SPEC.name]
        cb = ttk.Combobox(tmpl, textvariable=self.var_template, values=templates,
                           state="readonly", width=40)
        cb.pack(side=LEFT, padx=(8, 0))

        # ─── 페이지 여백 ───
        margins = TtkLabelFrame(content, text="페이지 여백 (mm)", padding=10)
        margins.pack(fill=X, pady=(0, 10))
        m = self.state.spec.margins
        self.var_margins = {
            "left":   tk.DoubleVar(value=m.left),
            "right":  tk.DoubleVar(value=m.right),
            "top":    tk.DoubleVar(value=m.top),
            "bottom": tk.DoubleVar(value=m.bottom),
            "header": tk.DoubleVar(value=m.header),
            "footer": tk.DoubleVar(value=m.footer),
        }
        labels = [("왼쪽", "left"), ("오른쪽", "right"), ("위", "top"),
                   ("아래", "bottom"), ("머리말", "header"), ("꼬리말", "footer")]
        for i, (label, key) in enumerate(labels):
            r, c = divmod(i, 3)
            ttk.Label(margins, text=label, width=8).grid(row=r, column=c*2, sticky=W, pady=3)
            sb = ttk.Spinbox(margins, from_=0, to=50, increment=0.5,
                              textvariable=self.var_margins[key], width=8)
            sb.grid(row=r, column=c*2 + 1, sticky=W, padx=(2, 16))

        # ─── 줄간격 (전체 일괄) ───
        line = TtkLabelFrame(content, text="줄간격 (%)", padding=10)
        line.pack(fill=X, pady=(0, 10))
        self.var_line_default = tk.IntVar(value=self.state.spec.line_spacing_default)
        ttk.Label(line, text="전체 일괄:").grid(row=0, column=0, sticky=W, padx=(0, 4))
        ttk.Spinbox(line, from_=100, to=300, increment=5,
                     textvariable=self.var_line_default, width=8).grid(row=0, column=1, sticky=W)
        ttk.Label(line, text="(본문·제목·stamp 모두 동일 적용)").grid(
            row=0, column=2, sticky=W, padx=(8, 0))

        # ─── 액션 버튼 ───
        actions = ttk.Frame(content)
        actions.pack(fill=X, pady=(8, 0))
        ttk.Button(actions, text="기본값으로 초기화 (보고서 1 spec)",
                    command=self._reset_to_default).pack(side=LEFT)
        ttk.Button(actions, text="설정 적용",
                    command=self._apply_to_spec).pack(side=RIGHT)

    # ------------------------------------------------------------ actions
    def _reset_to_default(self) -> None:
        """모든 필드를 보고서 1 spec 기본값으로 되돌림."""
        self.state.spec = REPORT1_SPEC.clone()
        self._reload_fields()

    def _reload_fields(self) -> None:
        """state.spec → UI 필드 재반영."""
        m = self.state.spec.margins
        self.var_margins["left"].set(m.left)
        self.var_margins["right"].set(m.right)
        self.var_margins["top"].set(m.top)
        self.var_margins["bottom"].set(m.bottom)
        self.var_margins["header"].set(m.header)
        self.var_margins["footer"].set(m.footer)
        self.var_line_default.set(self.state.spec.line_spacing_default)
        # 글머리·폰트는 단순화를 위해 재생성 안 함 (기본값 변경 시 UI 재시작 권장)

    def _apply_to_spec(self) -> None:
        """UI 필드 → state.spec 반영. 다음 변환부터 적용됨.

        ★ 폰트·글머리·주석은 realtime_tab 이 SSOT — 여기서는 처리 안 함.
        """
        from forge.formatter.templates import PageMargins
        self.state.spec.margins = PageMargins(
            left=float(self.var_margins["left"].get()),
            right=float(self.var_margins["right"].get()),
            top=float(self.var_margins["top"].get()),
            bottom=float(self.var_margins["bottom"].get()),
            header=float(self.var_margins["header"].get()),
            footer=float(self.var_margins["footer"].get()),
        )
        self.state.spec.line_spacing_default = int(self.var_line_default.get())

        # 사용자 피드백
        from tkinter import messagebox
        messagebox.showinfo("적용됨", "현재 양식 설정이 다음 변환부터 적용됩니다.")
