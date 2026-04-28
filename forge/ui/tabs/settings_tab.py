"""
탭 ① 기본정보 — 보고서 양식 spec 표시 + 사용자 테일러링.

★ 보고서명·작성부서·작성일 같은 문서 메타데이터는 여기 없음.
  그건 마크다운 front-matter 의 영역 (탭 ②).

여기서 다루는 것은 양식 spec 자체:
  - 보고서 템플릿 선택 (금감원페이지 외)
  - 페이지 여백 / 줄간격
  - 폰트·크기 (대제목·중제목·소제목·stamp)
  - 본문 글머리 4단계 (□ ○ - ·)

기본값은 forge.formatter.templates.REPORT1_SPEC (= 금감원페이지 표준).
사용자가 필드를 수정하면 self.state.spec 에 반영되어 탭 ② 변환에 사용됨.
"""
from __future__ import annotations

import tkinter as tk
from tkinter import ttk
from tkinter.constants import LEFT, RIGHT, BOTH, X, W, E
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

        # ─── 폰트 (대제목·중제목·소제목·본문) ───
        fonts = TtkLabelFrame(content, text="폰트·크기 (보고서 1 기본값)", padding=10)
        fonts.pack(fill=X, pady=(0, 10))
        font_rows = [
            ("대제목 (보고서명)", self.state.spec.title_font, self.state.spec.title_size_pt),
            ("중제목 (Ⅰ./Ⅱ.)",    self.state.spec.section_title_font, self.state.spec.section_title_size_pt),
            ("소제목 (가./나.)",   self.state.spec.subsection_font, self.state.spec.subsection_marker_size_pt),
            ("부서·일자 stamp",   self.state.spec.date_font, self.state.spec.date_size_pt),
        ]
        self.var_fonts: list[tuple[tk.StringVar, tk.DoubleVar]] = []
        for i, (label, font, size) in enumerate(font_rows):
            ttk.Label(fonts, text=label, width=18).grid(row=i, column=0, sticky=W, pady=2)
            v_font = tk.StringVar(value=font)
            v_size = tk.DoubleVar(value=size)
            ttk.Entry(fonts, textvariable=v_font, width=20).grid(row=i, column=1, sticky=W, padx=(0, 6))
            ttk.Label(fonts, text="크기:").grid(row=i, column=2, sticky=W)
            ttk.Spinbox(fonts, from_=6, to=72, increment=0.5,
                         textvariable=v_size, width=6).grid(row=i, column=3, sticky=W, padx=(2, 0))
            ttk.Label(fonts, text="pt").grid(row=i, column=4, sticky=W, padx=(2, 0))
            self.var_fonts.append((v_font, v_size))

        # ─── 본문 글머리 4단계 (□ ○ - ·) ───
        bullets = TtkLabelFrame(content,
                                 text="본문 글머리 (4단계: □ ○ - ·) — 모두 휴먼명조 15pt, 깊이만 누진",
                                 padding=10)
        bullets.pack(fill=X, pady=(0, 10))
        ttk.Label(bullets, text="md", width=6).grid(row=0, column=0, sticky=W)
        ttk.Label(bullets, text="출력", width=6).grid(row=0, column=1, sticky=W)
        ttk.Label(bullets, text="폰트", width=14).grid(row=0, column=2, sticky=W)
        ttk.Label(bullets, text="크기", width=8).grid(row=0, column=3, sticky=W)
        ttk.Label(bullets, text="내어쓰기", width=10).grid(row=0, column=4, sticky=W)
        self.var_bullets: list[dict] = []
        for i, b in enumerate(self.state.spec.bullets):
            vars_b = {
                "md":   tk.StringVar(value=b.md_glyph),
                "out":  tk.StringVar(value=b.out_glyph),
                "font": tk.StringVar(value=b.font),
                "size": tk.DoubleVar(value=b.size_pt),
                "indent": tk.DoubleVar(value=b.indent_pt),
            }
            ttk.Label(bullets, textvariable=vars_b["md"], width=6).grid(row=i+1, column=0, sticky=W)
            ttk.Entry(bullets, textvariable=vars_b["out"], width=6).grid(row=i+1, column=1, sticky=W)
            ttk.Entry(bullets, textvariable=vars_b["font"], width=14).grid(row=i+1, column=2, sticky=W, padx=(0, 4))
            ttk.Spinbox(bullets, from_=6, to=72, increment=0.5,
                         textvariable=vars_b["size"], width=8).grid(row=i+1, column=3, sticky=W)
            ttk.Spinbox(bullets, from_=-100, to=100, increment=0.2,
                         textvariable=vars_b["indent"], width=10).grid(row=i+1, column=4, sticky=W)
            self.var_bullets.append(vars_b)

        # ─── 주석 (단일 spec — *, ※(당구장), †(십자가) 모두 동일 처리) ───
        ann = TtkLabelFrame(content,
                              text="주석 (단일 spec — *, ※(당구장), †(십자가) 모두 동일)",
                              padding=10)
        ann.pack(fill=X, pady=(0, 10))
        a = self.state.spec.annotation
        self.var_annotation = {
            "font":   tk.StringVar(value=a.font),
            "size":   tk.DoubleVar(value=a.size_pt),
            "indent": tk.DoubleVar(value=a.indent_pt),
        }
        ttk.Label(ann, text="폰트:").grid(row=0, column=0, sticky=W, padx=(0, 4))
        ttk.Entry(ann, textvariable=self.var_annotation["font"], width=14).grid(row=0, column=1, sticky=W)
        ttk.Label(ann, text="크기:").grid(row=0, column=2, sticky=W, padx=(16, 4))
        ttk.Spinbox(ann, from_=6, to=72, increment=0.5,
                     textvariable=self.var_annotation["size"], width=8).grid(row=0, column=3, sticky=W)
        ttk.Label(ann, text="pt").grid(row=0, column=4, sticky=W, padx=(2, 0))
        ttk.Label(ann, text="내어쓰기:").grid(row=0, column=5, sticky=W, padx=(16, 4))
        ttk.Spinbox(ann, from_=-100, to=100, increment=0.2,
                     textvariable=self.var_annotation["indent"], width=10).grid(row=0, column=6, sticky=W)

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
        """UI 필드 → state.spec 반영. 다음 변환부터 적용됨."""
        from forge.formatter.templates import PageMargins, BulletStyle
        # 메타데이터는 state 가 아니라 markdown_tab 에서 읽음 (front-matter 우선)
        self.state.spec.margins = PageMargins(
            left=float(self.var_margins["left"].get()),
            right=float(self.var_margins["right"].get()),
            top=float(self.var_margins["top"].get()),
            bottom=float(self.var_margins["bottom"].get()),
            header=float(self.var_margins["header"].get()),
            footer=float(self.var_margins["footer"].get()),
        )
        self.state.spec.line_spacing_default = int(self.var_line_default.get())
        # 폰트 4행
        labels = ["title", "section_title", "subsection", "date"]
        for label, (v_font, v_size) in zip(labels, self.var_fonts):
            if label == "title":
                self.state.spec.title_font = v_font.get()
                self.state.spec.title_size_pt = float(v_size.get())
            elif label == "section_title":
                self.state.spec.section_title_font = v_font.get()
                self.state.spec.section_title_size_pt = float(v_size.get())
            elif label == "subsection":
                self.state.spec.subsection_font = v_font.get()
                self.state.spec.subsection_marker_size_pt = float(v_size.get())
            elif label == "date":
                self.state.spec.date_font = v_font.get()
                self.state.spec.date_size_pt = float(v_size.get())
        # 본문 글머리 4단계
        new_bullets = []
        for old, vars_b in zip(self.state.spec.bullets, self.var_bullets):
            new_bullets.append(BulletStyle(
                md_glyph=vars_b["md"].get(),
                out_glyph=vars_b["out"].get(),
                font=vars_b["font"].get(),
                size_pt=float(vars_b["size"].get()),
                indent_pt=float(vars_b["indent"].get()),
                bold=old.bold,
                space_above_pt=old.space_above_pt,
                line_spacing=old.line_spacing,
                fixed_pre=old.fixed_pre, fixed_post=old.fixed_post,
            ))
        self.state.spec.bullets = new_bullets

        # 주석 단일 spec
        old_a = self.state.spec.annotation
        self.state.spec.annotation = BulletStyle(
            md_glyph=old_a.md_glyph,
            out_glyph=old_a.out_glyph,
            font=self.var_annotation["font"].get(),
            size_pt=float(self.var_annotation["size"].get()),
            indent_pt=float(self.var_annotation["indent"].get()),
            bold=old_a.bold,
            space_above_pt=old_a.space_above_pt,
            line_spacing=old_a.line_spacing,
            fixed_pre=old_a.fixed_pre, fixed_post=old_a.fixed_post,
        )

        # 사용자 피드백
        from tkinter import messagebox
        messagebox.showinfo("적용됨", "현재 양식 설정이 다음 변환부터 적용됩니다.")
