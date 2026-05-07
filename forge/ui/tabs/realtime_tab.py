"""
탭 ③ 개별 작업 — 실시간 모드.

활성 한/글 문서에 STAGE 3 룰을 사용자가 버튼으로 골라 적용. 단일 문단
단위로 즉시 적용 + 디버그 로그 창에 단계별 진행 상황 누적 표시 — 사용자가
로그를 보고 어디서 fail 했는지 진단 가능.
"""
from __future__ import annotations

import threading
import tkinter as tk
from tkinter import ttk
from tkinter.constants import LEFT, RIGHT, X, BOTH, W
from tkinter.scrolledtext import ScrolledText
from tkinter.ttk import LabelFrame as TtkLabelFrame
from typing import TYPE_CHECKING, Callable, Optional

from forge.hwp_session import (
    MultipleHwpInstancesError,
    NoExistingHwpError,
    init_com_for_thread,
)
from forge.formatter import (
    NoSelectionError,
    convert_selection_to_hwpx,
)
from forge.linter import (
    adjust_kerning_current_paragraph,
    align_current_paragraph,
    fit_current_paragraph_to_one_line,
)

from ..tooltip import Tooltip

if TYPE_CHECKING:
    from ..app import ForgeApp


class RealtimeTab:
    def __init__(self, parent: tk.Misc, app: "ForgeApp"):
        self.app = app
        self.state = app.state
        self.frame = ttk.Frame(parent, padding=20)

        # 안내 헤더
        title = ttk.Label(
            self.frame,
            text="개별 작업 — 실시간 모드",
            font=("", 14, "bold"),
        )
        title.pack(anchor=W, pady=(0, 6))

        desc = ttk.Label(
            self.frame,
            text="활성 한/글 문서(.hwp 또는 .hwpx)에 STAGE 3 룰을 사용자가 버튼으로 골라 즉시 적용.\n"
                 "각 버튼 클릭 시 진행 상황이 아래 로그창에 누적 출력",
            wraplength=900, justify="left",
        )
        desc.pack(anchor=W, pady=(0, 12))

        # ─── 활성 그룹 — 현재 문단 단위 적용 ───────────────────
        # 버튼 vertical stack (좌측) + meta controls (우측 상단).
        # 행 0: 자동 정렬 (들·자·들)               — 메인 동선         hk Q
        # 행 1: 어절 1개 끌어올림                  — 보조 동선         hk W
        # 행 2: 폰트·크기 (본문, 휴먼명조 / 15)    — set_font 자동 dispatch  hk A
        # 행 3: 폰트·크기 (주석, 맑은 고딕 / 12)                          hk S
        # 행 4: 폰트·크기 (헤드라인, HY헤드라인M / 15)                    hk F
        # 행 5: 폰트·크기 (울릉도, HY울릉도M / 15)                        hk G
        # 행 6: 현재 문단 글자크기 (빈줄용, 사용자 지정 pt)               hk D
        # 행 7: 자간 0 초기화 (선택/문단)                                 hk Z
        # 행 8: 선택 영역 → 마크다운 변환                                 hk X
        # 행 9·10: 들여쓰기 정렬·자간조정          — 개별기능 표시 시만
        active_group = TtkLabelFrame(
            self.frame,
            text="🧪 현재 캐럿이 위치한 곳(문단) 또는 선택영역에만 적용됨",
            padding=10,
        )
        active_group.pack(fill=X, pady=(0, 6))

        # ─── 상단 meta bar — 로그 비우기 / 개별기능 표시 ─────
        top_meta = ttk.Frame(active_group)
        top_meta.pack(fill=X, pady=(0, 8))
        ttk.Button(
            top_meta, text="🧹 로그 비우기",
            command=self._clear_log,
            width=14,
        ).pack(side=LEFT)
        self.var_show_individual = tk.BooleanVar(value=False)
        ttk.Checkbutton(
            top_meta, text="개별기능 표시",
            variable=self.var_show_individual,
            command=self._toggle_individual_buttons,
        ).pack(side=LEFT, padx=(20, 0))

        # ★ 진단 도구는 코드/메서드는 보존하되 UI 노출만 off.
        #   - 🔬 캐럿 글자모양 (_inspect_caret_charshape)
        #   - 폰트 검색 (_search_installed_fonts) + var_font_search
        #   재노출 필요 시 아래 _DIAG_VISIBLE = True 로.
        self.var_font_search = tk.StringVar(value="휴먼|HY|명조|함초롬|TH|바탕|돋움")
        _DIAG_VISIBLE = False
        if _DIAG_VISIBLE:
            ttk.Button(
                top_meta, text="🔬 캐럿 글자모양",
                command=self._inspect_caret_charshape,
                width=16,
            ).pack(side=LEFT, padx=(20, 0))
            ttk.Label(top_meta, text="폰트 검색:").pack(side=LEFT, padx=(20, 4))
            ent = ttk.Entry(top_meta, textvariable=self.var_font_search, width=28)
            ent.pack(side=LEFT)
            ent.bind("<Return>", lambda _e: self._search_installed_fonts())
            ttk.Button(
                top_meta, text="🔎 검색",
                command=self._search_installed_fonts,
                width=8,
            ).pack(side=LEFT, padx=(2, 0))

        # ─── hotkey letter StringVars (사용자 편집 가능) + 상태 라벨 dict ────
        # 9 개 hotkey 각각 letter 1글자 — 비우면 비활성화. 변경 시 GlobalHotkeyManager
        # 에 PostThreadMessage 로 재등록 요청. 결과는 status 라벨 (✓ / ✗ / —) 로 표시.
        self.var_hk_letter: dict[int, tk.StringVar] = {
            1: tk.StringVar(value="Q"),  # 자동 정렬
            2: tk.StringVar(value="W"),  # 어절 끌어올림
            3: tk.StringVar(value="A"),  # 본문 폰트 (휴먼명조)
            4: tk.StringVar(value="S"),  # 주석 폰트 (맑은 고딕)
            5: tk.StringVar(value="F"),  # 헤드라인 폰트 (HY헤드라인M)
            6: tk.StringVar(value="G"),  # 울릉도 폰트 (HY울릉도M)
            7: tk.StringVar(value="D"),  # 현재 문단 글자크기 (빈줄용)
            8: tk.StringVar(value="Z"),  # 자간 0 초기화
            9: tk.StringVar(value="X"),  # 선택 영역 → 마크다운 변환
        }
        # 마지막으로 성공 적용된 letter — 실패 시 revert 기준
        self._hk_applied: dict[int, str] = {
            1: "Q", 2: "W", 3: "A", 4: "S",
            5: "F", 6: "G", 7: "D",
            8: "Z", 9: "X",
        }
        # 상태 라벨 위젯 (foreground 동적 변경 위해 reference 보관)
        self._hk_status_lbl: dict[int, ttk.Label] = {}

        # ─── 3-컬럼 grid: [버튼] [지정서식] [단축키] ────────────
        # 각 컬럼이 vertical 정렬되도록 grid 사용. 비어있는 셀(예: 행 1·2 의 지정서식)
        # 은 빈 placeholder Frame 으로 두면 column 폭이 다른 행에 맞춰 정렬됨.
        grid = ttk.Frame(active_group)
        grid.pack(fill=X, anchor="w")
        self._main_grid = grid
        # 컬럼 0: 버튼 — 충분한 minsize 로 모든 버튼 정렬
        # 컬럼 1: 지정서식 (font+size) — 비어있으면 minsize 만 점유
        # 컬럼 2: 단축키 — 좌측 끝 정렬
        grid.columnconfigure(0, minsize=200)
        grid.columnconfigure(1, minsize=180)
        grid.columnconfigure(2)

        # 폰트 입력 StringVars — 행 2~5 의 4 종 폰트 cluster.
        # 모두 사용자가 Combobox 에서 자유 선택. 기본값은 보고서 1 spec 관례.
        self.var_font1 = tk.StringVar(value="휴먼명조")     # 본문 (tool2 권위 dispatch)
        self.var_size1 = tk.StringVar(value="15")
        self.var_font2 = tk.StringVar(value="맑은 고딕")    # 주석
        self.var_size2 = tk.StringVar(value="12")
        self.var_font3 = tk.StringVar(value="HY헤드라인M")  # 헤드라인 (소제목/강조)
        self.var_size3 = tk.StringVar(value="15")
        self.var_font4 = tk.StringVar(value="HY울릉도M")    # 울릉도 (별도 강조)
        self.var_size4 = tk.StringVar(value="15")
        # 빈줄용 글자크기 — 행 6 button. 사용자가 칸에서 자유롭게 변경 가능.
        self.var_blank_size = tk.StringVar(value="8")

        # ─── 행 0: 자동 정렬 ────────────────────────────────────
        btn1 = ttk.Button(
            grid, text="자동 정렬 (들·자·들)",
            command=self._run_kerning_then_indent,
            width=24,
        )
        btn1.grid(row=0, column=0, sticky="w", pady=2, padx=(0, 8))
        Tooltip(btn1, "문서기호 이후 들여쓰기 조정 + 어절 잘리지 않게 자간조정 동시 실행")
        # column 1 비어있음 — placeholder 안 둬도 columnconfigure minsize 가 잡아줌
        hk1 = ttk.Frame(grid)
        self._build_hotkey_widget(hk1, hk_id=1)
        hk1.grid(row=0, column=2, sticky="w")

        # ─── 행 1: 어절 1개 끌어올림 ───────────────────────────
        btn2 = ttk.Button(
            grid, text="어절 1개 끌어올림 (자간)",
            command=lambda: self._run_paragraph_rule(
                "어절 끌어올림", fit_current_paragraph_to_one_line,
            ),
            width=24,
        )
        btn2.grid(row=1, column=0, sticky="w", pady=2, padx=(0, 8))
        Tooltip(btn2, "1개 어절이 다음 줄에 튀어나온 경우 자간을 좁혀서 위로 올리기")
        hk2 = ttk.Frame(grid)
        self._build_hotkey_widget(hk2, hk_id=2)
        hk2.grid(row=1, column=2, sticky="w")

        # ─── 행 2: 폰트·크기 (본문) — 휴먼명조 15pt 등 본문체 ────
        btn3 = ttk.Button(
            grid, text="폰트·크기 (본문)",
            command=lambda: self._apply_font(self.var_font1.get(), self.var_size1.get()),
            width=24,
        )
        btn3.grid(row=2, column=0, sticky="w", pady=2, padx=(0, 8))
        Tooltip(btn3, "선택영역 폰트·크기 (본문) — 우측 입력값 적용")
        font_cluster_1 = self._make_font_cluster(grid, self.var_font1, self.var_size1)
        font_cluster_1.grid(row=2, column=1, sticky="w", padx=(0, 8))
        hk3 = ttk.Frame(grid)
        self._build_hotkey_widget(hk3, hk_id=3)
        hk3.grid(row=2, column=2, sticky="w")

        # ─── 행 3: 폰트·크기 (주석) — 맑은 고딕 12pt 등 주석체 ────
        btn4 = ttk.Button(
            grid, text="폰트·크기 (주석)",
            command=lambda: self._apply_font(self.var_font2.get(), self.var_size2.get()),
            width=24,
        )
        btn4.grid(row=3, column=0, sticky="w", pady=2, padx=(0, 8))
        Tooltip(btn4, "선택영역 폰트·크기 (주석) — 우측 입력값 적용")
        font_cluster_2 = self._make_font_cluster(grid, self.var_font2, self.var_size2)
        font_cluster_2.grid(row=3, column=1, sticky="w", padx=(0, 8))
        hk4 = ttk.Frame(grid)
        self._build_hotkey_widget(hk4, hk_id=4)
        hk4.grid(row=3, column=2, sticky="w")

        # ─── 행 4: 폰트·크기 (헤드라인) — HY헤드라인M 15pt ──
        btn5 = ttk.Button(
            grid, text="폰트·크기 (헤드라인)",
            command=lambda: self._apply_font(self.var_font3.get(), self.var_size3.get()),
            width=24,
        )
        btn5.grid(row=4, column=0, sticky="w", pady=2, padx=(0, 8))
        Tooltip(btn5, "선택영역 폰트·크기 (헤드라인) — 우측 입력값 적용")
        font_cluster_3 = self._make_font_cluster(grid, self.var_font3, self.var_size3)
        font_cluster_3.grid(row=4, column=1, sticky="w", padx=(0, 8))
        hk5 = ttk.Frame(grid)
        self._build_hotkey_widget(hk5, hk_id=5)
        hk5.grid(row=4, column=2, sticky="w")

        # ─── 행 5: 폰트·크기 (울릉도) — HY울릉도M 15pt ──
        # 별도 강조용 폰트. 헤드라인과 거의 동일한 동선이지만 독립 var/hotkey.
        btn5b = ttk.Button(
            grid, text="폰트·크기 (울릉도)",
            command=lambda: self._apply_font(self.var_font4.get(), self.var_size4.get()),
            width=24,
        )
        btn5b.grid(row=5, column=0, sticky="w", pady=2, padx=(0, 8))
        Tooltip(btn5b, "선택영역 폰트·크기 (울릉도) — 우측 입력값 적용")
        font_cluster_4 = self._make_font_cluster(grid, self.var_font4, self.var_size4)
        font_cluster_4.grid(row=5, column=1, sticky="w", padx=(0, 8))
        hk6 = ttk.Frame(grid)
        self._build_hotkey_widget(hk6, hk_id=6)
        hk6.grid(row=5, column=2, sticky="w")

        # ─── 행 6: 현재 문단 글자크기 (빈줄용, 사용자 지정) ────
        # 사용자가 [Entry] pt 칸에 원하는 크기를 입력 → 클릭/단축키 시 그 값으로 적용.
        # 기본 8pt. 한/글에서 빈줄 자간 꼬임 회피용 작은 크기 세팅 동선.
        btn6 = ttk.Button(
            grid, text="현재 문단 → 글자크기",
            command=self._run_paragraph_size_8,
            width=24,
        )
        btn6.grid(row=6, column=0, sticky="w", pady=2, padx=(0, 8))
        Tooltip(btn6, "빈줄 용 글자크기 설정 (자간 꼬임 회피)")
        size_cluster = self._make_size_cluster(grid, self.var_blank_size)
        size_cluster.grid(row=6, column=1, sticky="w", padx=(0, 8))
        hk7 = ttk.Frame(grid)
        self._build_hotkey_widget(hk7, hk_id=7)
        hk7.grid(row=6, column=2, sticky="w")

        # ─── 행 7: 자간 0 초기화 (선택 영역 또는 현재 문단) ────
        btn7 = ttk.Button(
            grid, text="자간 0 초기화 (선택/문단)",
            command=self._run_kerning_reset,
            width=24,
        )
        btn7.grid(row=7, column=0, sticky="w", pady=2, padx=(0, 8))
        Tooltip(btn7, "해당 문단 자간 0으로 초기화 (자간 꼬였을 때)")
        hk8 = ttk.Frame(grid)
        self._build_hotkey_widget(hk8, hk_id=8)
        hk8.grid(row=7, column=2, sticky="w")

        # ─── 행 8: 선택 영역 → 마크다운 변환 (영역 필수) ────────
        # 한/글 자체에서 md 본문을 타이핑한 뒤 영역 선택 → 단축키 호출.
        # 선택 영역 plain text 를 추출해 cursor 모드로 그 자리에 변환 출력.
        # Tk Text 의 한글 IME 매끄럽지 않음 회피 — 한/글 IME 가 매끄럽다.
        btn8 = ttk.Button(
            grid, text="선택 영역 → 마크다운 변환",
            command=self._run_md_convert_selection,
            width=24,
        )
        btn8.grid(row=8, column=0, sticky="w", pady=2, padx=(0, 8))
        Tooltip(btn8,
                "한/글에서 영역 선택 후 호출 — 선택 텍스트를 마크다운으로 해석해 변환 결과로 대체")
        hk9 = ttk.Frame(grid)
        self._build_hotkey_widget(hk9, hk_id=9)
        hk9.grid(row=8, column=2, sticky="w")

        # ─── 행 8·9: 개별 진단 버튼 (체크박스 토글, 기본 숨김) ──
        # 토글 시 grid_remove() / grid() 로 노출 제어.
        self.btn_indent_only = ttk.Button(
            grid, text="들여쓰기 정렬",
            command=lambda: self._run_paragraph_rule(
                "들여쓰기 정렬", align_current_paragraph,
            ),
            width=24,
        )
        self.btn_kerning_only = ttk.Button(
            grid, text="자간조정 (어절 잘림 방지)",
            command=lambda: self._run_paragraph_rule(
                "자간조정", adjust_kerning_current_paragraph,
            ),
            width=28,
        )
        # 초기 미체크 — grid 에 올리지 않음 (체크 시 _toggle_individual_buttons 가 grid)

        # ─── 디버그 로그 창 ──────────────────────────────────
        log_frame = TtkLabelFrame(self.frame, text="📋 디버그 로그", padding=6)
        log_frame.pack(fill=X, pady=(6, 12))

        self.log_text = ScrolledText(
            log_frame, height=12, wrap="none",
            font=("Consolas", 9),
        )
        self.log_text.pack(fill=X, expand=False)

        # ─── 미구현 placeholder 그룹들 (현재 숨김) ────────────────
        # 각 그룹의 버튼들은 후속 작업에서 forge.rules.polisher 에 연결 예정.
        # 구현 완료 시 _SHOW_PLACEHOLDERS = True 로 바꾸면 다시 노출됨.
        _SHOW_PLACEHOLDERS = False
        groups = [
            ("📐 페이지·여백", [
                "여백 표준화 (보고서 1)",
                "쪽번호 초기화",
                "쪽번호 숨기기 / 보이기",
            ]),
            ("✏️ 글자·문단", [
                "휴먼명조 본문 적용",
                "맑은 고딕 본문 적용",
                "줄간격 150% / 120%",
            ]),
            ("📊 표 정형", [
                "셀 여백 0",
                "표 테두리 단순선",
                "합계수식 삽입",
            ]),
            ("🔧 블록 편집", [
                "다중 바꾸기",
                "여백·엔터 정리",
                "숫자 → 한글화",
            ]),
        ]

        if _SHOW_PLACEHOLDERS:
            for group_label, buttons in groups:
                group = TtkLabelFrame(self.frame, text=group_label, padding=10)
                group.pack(fill=X, pady=(0, 6))
                for label in buttons:
                    btn = ttk.Button(
                        group, text=label,
                        command=lambda l=label: self._not_implemented(l),
                        width=28,
                    )
                    btn.pack(side=LEFT, padx=(0, 4))

            footer = ttk.Label(
                self.frame,
                text="※ 위 활성 그룹 외에는 골격만 노출. 후속 작업에서 forge.rules.polisher 에 연결.",
            )
            footer.pack(anchor=W, pady=(8, 0))

    # ----------------------------------------- hotkey 어댑터 (app.py 가 호출)
    def hotkey_auto_align(self) -> None:
        """Ctrl+Shift+Q — 자동 정렬 (들·자·들)."""
        self._run_kerning_then_indent()

    def hotkey_word_pull(self) -> None:
        """Ctrl+Shift+W — 어절 1개 끌어올림."""
        self._run_paragraph_rule(
            "어절 끌어올림", fit_current_paragraph_to_one_line,
        )

    def hotkey_font_1(self) -> None:
        """Ctrl+Shift+A — 행 3 폰트/크기 적용 (StringVar 의 현재값 그대로)."""
        self._apply_font(self.var_font1.get(), self.var_size1.get())

    def hotkey_font_2(self) -> None:
        """Ctrl+Shift+S — 행 4 폰트/크기 적용."""
        self._apply_font(self.var_font2.get(), self.var_size2.get())

    def hotkey_headline_font(self) -> None:
        """Ctrl+Shift+F — 행 4 헤드라인 폰트/크기 적용 (var_font3 = HY헤드라인M)."""
        self._apply_font(self.var_font3.get(), self.var_size3.get())

    def hotkey_uleungdo_font(self) -> None:
        """Ctrl+Shift+G — 행 5 울릉도 폰트/크기 적용 (var_font4 = HY울릉도M)."""
        self._apply_font(self.var_font4.get(), self.var_size4.get())

    def hotkey_paragraph_size_8(self) -> None:
        """Ctrl+Shift+D — 현재 문단 글자크기 (var_blank_size pt, 기본 8)."""
        self._run_paragraph_size_8()

    def hotkey_kerning_reset(self) -> None:
        """Ctrl+Shift+Z — 자간 0 초기화 (선택 영역 또는 현재 문단)."""
        self._run_kerning_reset()

    def hotkey_md_convert_selection(self) -> None:
        """Ctrl+Shift+X — 한/글 선택 영역을 마크다운으로 해석해 그 자리에 변환 출력."""
        self._run_md_convert_selection()

    # ----------------------------------------- 설치 폰트 목록 (lazy + 캐시)
    def _get_installed_fonts(self) -> list[str]:
        """설치된 폰트 목록 반환. 첫 호출 시 enum 후 캐시.

        tkinter.font.families() 는 GDI/DirectWrite enum 결과로 한/글 폰트
        드롭다운과 사실상 동일. 사용자가 Combobox 에서 정확한 face name 을
        직접 골라 사용 → 이름 오타 사고 원천 차단.
        """
        if not hasattr(self, "_font_families_cache"):
            try:
                from tkinter import font as tkfont
                self._font_families_cache = sorted(
                    set(tkfont.families(self.app.root))
                )
            except Exception as e:
                print(f"[realtime_tab] font enum 실패: {e}")
                self._font_families_cache = []
        return self._font_families_cache

    # ----------------------------------------- 폰트/크기 입력 묶음 (지정서식 컬럼)
    def _make_font_cluster(
        self,
        parent: tk.Misc,
        font_var: tk.StringVar,
        size_var: tk.StringVar,
    ) -> ttk.Frame:
        """폰트 Combobox + 크기 Entry + 'pt' 라벨을 한 Frame 에 모아 반환.

        Combobox: 설치 폰트 드롭다운 + 자유 타이핑 둘 다 허용 (state='normal').
        반환된 Frame 을 grid 셀에 그대로 배치해 컬럼 정렬에 활용.
        """
        cluster = ttk.Frame(parent)
        cb = ttk.Combobox(
            cluster, textvariable=font_var, width=16,
            values=self._get_installed_fonts(),
        )
        cb.pack(side=LEFT, padx=(0, 4))
        ttk.Entry(cluster, textvariable=size_var, width=5).pack(side=LEFT)
        ttk.Label(cluster, text="pt", foreground="#777").pack(
            side=LEFT, padx=(2, 0))
        return cluster

    def _make_size_cluster(
        self,
        parent: tk.Misc,
        size_var: tk.StringVar,
    ) -> ttk.Frame:
        """크기 Entry + 'pt' 라벨만. font_cluster 와 같은 column 정렬용."""
        cluster = ttk.Frame(parent)
        ttk.Entry(cluster, textvariable=size_var, width=5).pack(side=LEFT)
        ttk.Label(cluster, text="pt", foreground="#777").pack(
            side=LEFT, padx=(2, 0))
        return cluster

    # ----------------------------------------- hotkey letter 편집 위젯
    def _build_hotkey_widget(self, parent: tk.Misc, hk_id: int) -> None:
        """`Ctrl+Shift+[Q]  ✓` 형태로 한 행 우측에 hotkey 편집 + 상태 노출.

        Entry 폭 3자, justify=center. <FocusOut> 와 <Return> 에서 _commit_hotkey 호출.
        상태 라벨은 ✓ (등록됨, green) / ✗ (실패, red) / — (비활성, gray) 표시.
        """
        ttk.Label(parent, text="Ctrl+Shift+", foreground="#888").pack(side=LEFT)
        var = self.var_hk_letter[hk_id]
        entry = ttk.Entry(
            parent, textvariable=var, width=3, justify="center",
        )
        entry.pack(side=LEFT)
        entry.bind("<FocusOut>", lambda e, h=hk_id: self._commit_hotkey(h))
        entry.bind("<Return>",   lambda e, h=hk_id: self._commit_hotkey(h))

        status = ttk.Label(parent, text="✓", foreground="#1d8a1d")  # green
        status.pack(side=LEFT, padx=(4, 0))
        self._hk_status_lbl[hk_id] = status

    def _commit_hotkey(self, hk_id: int) -> None:
        """Entry 변경 commit — 새 letter 로 hotkey 재등록 시도.

        규칙:
          - 빈 문자열 → 비활성화
          - 1자리 영문/숫자 → 재등록 시도
          - 그 외 → 직전 적용값으로 revert
          - 재등록 실패 (다른 앱이 잡음 / 내부 중복) → 직전 적용값으로 revert + ✗ 표시
        """
        var = self.var_hk_letter[hk_id]
        raw = var.get().strip()
        # 입력 정규화 — 영문은 대문자
        candidate = raw.upper()

        if candidate == "":
            # 비활성화
            ok = self.app.hotkey_mgr.replace(hk_id, None, "(disabled)")
            self._set_hk_status(hk_id, "—", "#888")
            self._hk_applied[hk_id] = ""
            return

        # 검증 — 1자리 영숫자만
        valid = (len(candidate) == 1 and (candidate.isalpha() or candidate.isdigit()))
        if not valid:
            # revert
            prev = self._hk_applied[hk_id]
            var.set(prev)
            self._log(f"[hotkey] hk{hk_id}: invalid letter {raw!r} — revert to {prev!r}")
            return

        # 정규화된 값으로 Entry 동기화 (소문자 q → Q 등)
        if var.get() != candidate:
            var.set(candidate)

        # 동일 letter 면 no-op
        if candidate == self._hk_applied[hk_id]:
            return

        new_label = f"Ctrl+Shift+{candidate}"
        ok = self.app.hotkey_mgr.replace(hk_id, ord(candidate), new_label)
        if ok:
            self._hk_applied[hk_id] = candidate
            self._set_hk_status(hk_id, "✓", "#1d8a1d")
            self._log(f"[hotkey] hk{hk_id} ← Ctrl+Shift+{candidate} (등록됨)")
        else:
            # 실패 — Entry 를 직전 적용값으로 revert
            prev = self._hk_applied[hk_id]
            var.set(prev)
            self._set_hk_status(hk_id, "✗", "#c33")
            self._log(f"[hotkey] hk{hk_id} 등록 실패 (충돌?) — {prev!r} 유지")

    def _set_hk_status(self, hk_id: int, text: str, color: str) -> None:
        lbl = self._hk_status_lbl.get(hk_id)
        if lbl is None:
            return
        lbl.configure(text=text, foreground=color)

    def set_initial_hk_results(self, results: list[tuple[str, bool]]) -> None:
        """app 이 hotkey 초기 등록 후 호출 — 각 행에 ✓/✗ 반영.

        results 는 hk_id 1~9 순서. 비활성화는 startup 에 없으므로 ✓ or ✗ 만.
        """
        for idx, (label, ok) in enumerate(results):
            hk_id = idx + 1
            if hk_id not in self._hk_status_lbl:
                continue
            if ok:
                self._set_hk_status(hk_id, "✓", "#1d8a1d")
            else:
                self._set_hk_status(hk_id, "✗", "#c33")

    # ------------------------------------------------------------ 개별 버튼 토글
    def _toggle_individual_buttons(self) -> None:
        """'개별기능 표시' 체크박스 — 진단용 단일 룰 버튼 2개 표시/숨김.

        체크 시: 들여쓰기 정렬·자간조정 버튼을 grid 의 행 8·9 column 0 에 노출.
        해제 시: grid_remove() — grid 에서 빠지지만 내부 grid 옵션은 보존되어
        재체크 시 원래 위치에 grid() 로 즉시 복원 가능.
        """
        if self.var_show_individual.get():
            self.btn_indent_only.grid(
                row=9, column=0, sticky="w", pady=2, padx=(0, 8))
            self.btn_kerning_only.grid(
                row=10, column=0, sticky="w", pady=2, padx=(0, 8))
        else:
            self.btn_indent_only.grid_remove()
            self.btn_kerning_only.grid_remove()

    # ------------------------------------------------------------ 캐럿 글자모양
    def _inspect_caret_charshape(self) -> None:
        """현재 한/글 캐럿/선택영역의 CharShape 를 readback 해 로그에 표시.

        용도: 폰트 적용 직후 한/글이 받아들인 정확한 face name 확인.
        한/글이 face name 매칭에 실패하면 FaceNameHangul 이 빈 칸 — 즉시 감지.
        백그라운드 thread (COM init + ensure_hwp) — UI 멈춤 방지.
        """
        self.app._set_status("[inspect] 현재 캐럿 글자모양 조회 중...")
        threading.Thread(
            target=self._inspect_caret_charshape_async, daemon=True,
        ).start()

    def _inspect_caret_charshape_async(self) -> None:
        self._log("")
        self._log("━━━━━━ 현재 캐럿 글자모양 ━━━━━━")
        try:
            init_com_for_thread()
            try:
                session = self.app.ensure_hwp()
            except Exception as e:
                self._log(f"[hwp] attach 실패: {e}")
                self.app._set_status(f"✘ 조회 실패: {e}")
                return
            hwp = session.hwp

            # ─── (A) default CharShape readback (style 계층 결과) ───
            self._dump_charshape(hwp, label="default (GetDefault)")

            # ─── (B) selection readback — 캐럿 우측 1글자 선택 후 readback ───
            # GetDefault 는 style 계층에서만 채워질 수 있어 character-level
            # override 를 못 잡는 케이스가 있음. 선택영역 기반 readback 은
            # 그 글자의 실제 적용 face name 을 더 신뢰성 있게 반환.
            try:
                origin = hwp.GetPos()
                hwp.Run("MoveSelRight")
                hwp.HAction.GetDefault("CharShape", hwp.HParameterSet.HCharShape.HSet)
                self._dump_charshape(hwp, label="selection (1글자 우측)")
                hwp.Run("Cancel")
                if origin is not None:
                    hwp.SetPos(*origin)
            except Exception as e:
                self._log(f"  [selection-readback] 실패: {e}")

            # ─── (C) GetFontList — 문서에 사용된 face name 전체 (canonical) ───
            # 한/글이 자체적으로 인식한 정확한 이름 노출. ScanFont 선행 필수.
            try:
                self._log("")
                self._log("  ─── 문서 사용 글꼴 (ScanFont + GetFontList) ─")
                hwp.ScanFont()
                lang_map = [
                    (0, "한글"), (1, "영문"), (2, "한자"),
                    (3, "일어"), (4, "외국어"), (5, "기호"), (6, "사용자"),
                ]
                for lang_id, lang_name in lang_map:
                    try:
                        s = str(hwp.GetFontList(lang_id) or "")
                    except Exception as e:
                        self._log(f"  [{lang_name}] GetFontList({lang_id}) 실패: {e}")
                        continue
                    if not s:
                        self._log(f"  [{lang_name}] (없음)")
                    else:
                        self._log(f"  [{lang_name}] {s!r}")
            except Exception as e:
                self._log(f"  [GetFontList] 호출 실패: {e}")

            self._log("")
            self._log(
                "  💡 (A)·(B) 가 모두 빈 값이어도 한컴 API 의 readback 한계라 정상."
                " HAction.GetDefault 는 '초기 default' 만 채우고 caret 의 effective"
                " CharShape 를 readback 하지 않음 (tool2 도 readback path 없음)."
                " apply 자체가 정상 작동했는지는 한/글 화면 시각으로 확인하고,"
                " 문서가 사용 중인 face name 의 권위적 list 는 (C) GetFontList 결과"
                " 에서 확인."
            )
            self.app._set_status("✔ 캐럿 글자모양 조회 완료 — 로그 확인")
        except Exception as e:
            self._log(f"[ERROR] {type(e).__name__}: {e}")
            self.app._set_status(f"✘ 조회 실패: {e}")

    def _dump_charshape(self, hwp, label: str) -> None:
        """현재 hwp.HParameterSet.HCharShape 의 주요 항목을 로그에 dump."""
        cs = hwp.HParameterSet.HCharShape
        height = int(cs.Height or 0)
        size_pt = height / 100.0 if height else 0.0
        face_h = str(cs.FaceNameHangul or "")
        face_l = str(cs.FaceNameLatin or "")
        ft_h = int(cs.FontTypeHangul or 0)
        bold = bool(int(cs.Bold or 0))
        italic = bool(int(cs.Italic or 0))
        ratio_h = int(cs.RatioHangul or 100)
        spacing_h = int(cs.SpacingHangul or 0)
        ft_label = {0: "don't care", 1: "TTF", 2: "HFT"}.get(ft_h, f"?({ft_h})")
        self._log(f"  ─── {label} ─────")
        self._log(f"    크기      : {size_pt:.1f} pt  (Height={height})")
        self._log(f"    Bold/Italic: {bold} / {italic}")
        self._log(f"    자간/장평 : {spacing_h}% / {ratio_h}%")
        self._log(f"    FontType (한글) : {ft_h} ({ft_label})")
        self._log(f"    FaceNameHangul  : {face_h!r}")
        self._log(f"    FaceNameLatin   : {face_l!r}")
        self._log(f"    FaceNameHanja   : {str(cs.FaceNameHanja or '')!r}")
        self._log(f"    FaceNameJapanese: {str(cs.FaceNameJapanese or '')!r}")
        self._log(f"    FaceNameOther   : {str(cs.FaceNameOther or '')!r}")
        self._log(f"    FaceNameSymbol  : {str(cs.FaceNameSymbol or '')!r}")
        self._log(f"    FaceNameUser    : {str(cs.FaceNameUser or '')!r}")

    # ------------------------------------------------------------ 폰트 검색
    def _search_installed_fonts(self) -> None:
        """시스템에 설치된 폰트 중 정규식 매칭 항목을 로그에 출력.

        한/글 거치지 않고 tkinter.font.families() — Tk 가 enum 한 폰트 이름이
        곧 한/글이 인식하는 이름과 사실상 동일 (둘 다 GDI/DirectWrite enum 결과).
        용도: "이름이 안 먹어요" 진단 — 실제 설치된 정확한 face name 확인.
        """
        import re
        from tkinter import font as tkfont
        pat = self.var_font_search.get().strip() or ".+"
        try:
            rx = re.compile(pat, re.IGNORECASE)
        except re.error as e:
            self._log(f"[font-search] 정규식 오류: {e}")
            return
        try:
            fams = sorted(set(tkfont.families(self.app.root)))
        except Exception as e:
            self._log(f"[font-search] families() 호출 실패: {e}")
            return
        matches = [f for f in fams if rx.search(f)]
        self._log("")
        self._log(f"━━━━━━ 폰트 검색 (pattern={pat!r}) ━━━━━━")
        self._log(f"  설치 폰트 총 {len(fams)}개 / 매칭 {len(matches)}개")
        for f in matches:
            self._log(f"  • {f!r}")
        if not matches:
            self._log("  (매칭 없음 — pattern 비우거나 .+ 로 전체 보기)")

    # ------------------------------------------------------------ 로그
    def _log(self, msg: str) -> None:
        """GUI 스레드 안전 로그 추가."""
        def append():
            try:
                # ScrolledText 의 내부 Text 위젯은 .text 속성으로 접근
                inner = getattr(self.log_text, "text", self.log_text)
                inner.insert("end", msg + "\n")
                inner.see("end")
            except Exception:
                pass
        try:
            self.app.root.after(0, append)
        except Exception:
            pass

    def _clear_log(self) -> None:
        try:
            inner = getattr(self.log_text, "text", self.log_text)
            inner.delete("1.0", "end")
        except Exception:
            pass

    # ------------------------------------------------------------ 활성 핸들러
    def _run_paragraph_rule(self, label: str, fn: Callable) -> None:
        """현재 커서 위치 문단에 룰 1개 적용. 백그라운드 + 로그."""
        self.app._set_status(f"[STAGE 3] {label} 적용 중...")
        threading.Thread(
            target=self._run_async, args=(label, fn), daemon=True,
        ).start()

    def _run_async(self, label: str, fn: Callable) -> None:
        from tkinter import messagebox
        self._log("")
        self._log(f"━━━━━━ {label} 시작 ━━━━━━")
        try:
            init_com_for_thread()
            try:
                # ★ ensure_hwp 사용 — 살아있는 세션이면 재사용. 매 클릭마다
                # attach_or_create 를 새로 부르면 그 안의 Visible=True 가 한/글
                # 윈도우 크기를 자동화 default geometry 로 reset 하는 부작용 발생.
                session = self.app.ensure_hwp()
                self._log(f"[hwp] session: {session.version_name} #{session.instance_index} "
                          f"(is_new={session.is_new})")
            except MultipleHwpInstancesError as e:
                # 다중 인스턴스 — UI 스레드에서 picker 띄우고 사용자에게 재클릭 요청
                self._log(f"[hwp] 다중 인스턴스 ({len(e.instances)}개) — picker 표시")
                self.app._set_status(f"⚠ {label} 보류: 한/글 인스턴스 선택 필요")
                self.app.prompt_pick_from_worker(e.instances)
                return
            except NoExistingHwpError as e:
                self._log(f"[hwp] 한/글 미실행: {e}")
                self.app._set_status(f"✘ {label} 실패: 한/글 미실행")
                self.app.root.after(0, lambda: messagebox.showwarning(
                    "한/글 미실행",
                    "떠 있는 한/글 인스턴스가 없습니다.\n"
                    "한/글을 먼저 실행하시고 '한/글 선택' 버튼으로 연결해 주세요.",
                ))
                return
            except Exception as e:
                self._log(f"[hwp] attach 실패: {e}")
                self.app._set_status(f"✘ {label} 실패: 한/글 attach 불가")
                self.app.root.after(0, lambda: messagebox.showerror(
                    "한/글 연결 실패",
                    f"한/글 인스턴스 연결 중 오류가 발생했습니다.\n\n세부: {e}",
                ))
                return
            # fn 에 log callback 전달
            fn(session.hwp, log=self._log)
            self._log(f"━━━━━━ {label} 완료 ━━━━━━")
            done_msg = f"✔ {label} 적용 완료 (현재 문단)"
            self._log(done_msg)
            self.app._set_status(done_msg)
        except Exception as e:
            self._log(f"[ERROR] {type(e).__name__}: {e}")
            self.app._set_status(f"✘ {label} 실패: {e}")

    # ------------------------------------------------------------ 폰트/크기 적용
    def _apply_font(self, font_name: str, size_str: str) -> None:
        """선택 영역 또는 캐럿 위치의 폰트·크기 변경. 백그라운드 + 로그.

        Bold/Italic/색상 등은 건드리지 않음 (FaceName* + Height 만 set_param).
        선택 영역 있으면 selection 에 적용, 없으면 typing attr 변경 (HWP 기본 동작).
        """
        font_name = (font_name or "").strip()
        size_str = (size_str or "").strip()
        label = f"{font_name} {size_str}pt"
        self.app._set_status(f"[STAGE 3] 폰트·크기 적용: {label}")
        threading.Thread(
            target=self._run_font_apply_async,
            args=(font_name, size_str),
            daemon=True,
        ).start()

    def _run_font_apply_async(self, font_name: str, size_str: str) -> None:
        from tkinter import messagebox
        # set_font 가 '휴먼명조' 인 경우 tool2 권위 spec (set_font_humanmyongjo,
        # 7면 매핑 + HFT 강제) 으로 자동 dispatch — CLAUDE.md §3.2.
        from forge.renderers.primitives import set_font

        label = f"{font_name} {size_str}pt"
        self._log("")
        self._log(f"━━━━━━ 폰트·크기 적용 — {label} 시작 ━━━━━━")

        # 입력 검증
        if not font_name:
            self._log("[ERR] 폰트 이름이 비어있음")
            self.app._set_status("✘ 폰트 이름 비어있음")
            return
        try:
            size_pt = float(size_str)
            if size_pt <= 0:
                raise ValueError("크기는 양수여야 함")
        except (ValueError, TypeError):
            self._log(f"[ERR] 크기 파싱 실패: {size_str!r}")
            self.app._set_status(f"✘ 크기 입력 오류: {size_str!r}")
            return

        try:
            init_com_for_thread()
            try:
                session = self.app.ensure_hwp()
                self._log(f"[hwp] session: {session.version_name} #{session.instance_index}")
            except MultipleHwpInstancesError as e:
                self._log(f"[hwp] 다중 인스턴스 ({len(e.instances)}개) — picker 표시")
                self.app._set_status("⚠ 폰트 적용 보류: 한/글 인스턴스 선택 필요")
                self.app.prompt_pick_from_worker(e.instances)
                return
            except NoExistingHwpError as e:
                self._log(f"[hwp] 한/글 미실행: {e}")
                self.app._set_status("✘ 폰트 적용 실패: 한/글 미실행")
                self.app.root.after(0, lambda: messagebox.showwarning(
                    "한/글 미실행",
                    "떠 있는 한/글 인스턴스가 없습니다.\n"
                    "한/글을 먼저 실행하시고 '한/글 선택' 버튼으로 연결해 주세요.",
                ))
                return
            except Exception as e:
                self._log(f"[hwp] attach 실패: {e}")
                # self.app._set_status(f"✘ 폰트 적용 실패: {e}")
                return

            hwp = session.hwp
            # set_font 단일 진입점 — '휴먼명조' 면 set_font_humanmyongjo 로 자동
            # dispatch (tool2 권위 7면 매핑 + HFT). 그 외 일반 폰트는 7면 일괄 +
            # FontType=0 (don't care, 한/글 자동 매칭).
            set_font(hwp, font_name, size_pt)
            self._log(f"[ok] CharShape 적용 요청: {label}")
            # 진단 — 적용 후 실제 CharShape 를 다시 읽어 user 가 입력한 폰트가
            # 그대로 들어갔는지 확인. 한/글이 미설치 폰트는 silent substitution
            # 하지 않고 보통 그 이름 그대로 저장하나, 화면 표시는 폴백 폰트로
            # 됨. 이 readback 으로 "폰트가 안 먹는" 사고 (잘못된 이름) 진단.
            try:
                hwp.HAction.GetDefault("CharShape", hwp.HParameterSet.HCharShape.HSet)
                cs = hwp.HParameterSet.HCharShape
                self._log(
                    f"[verify] FaceNameHangul={cs.FaceNameHangul!r} "
                    f"Latin={cs.FaceNameLatin!r} Height={cs.Height} "
                    f"(요청 Height={int(size_pt * 100)})"
                )
                if str(cs.FaceNameHangul) != font_name:
                    self._log(
                        f"  ⚠ 요청 폰트 {font_name!r} 와 readback {cs.FaceNameHangul!r} "
                        f"불일치 — 한/글에 해당 폰트가 설치되지 않았거나 이름 오타 "
                        f"가능. 한/글 메뉴 [서식 → 글자 모양] 에서 실제 폰트 목록 확인."
                    )
            except Exception as ve:
                self._log(f"[verify] readback 실패: {ve}")
            self._log(f"━━━━━━ 폰트·크기 적용 — {label} 완료 ━━━━━━")
            done_msg = f"✔ 폰트·크기 적용 완료: {label}"
            self._log(done_msg)
            # self.app._set_status(done_msg)
        except Exception as e:
            self._log(f"[ERROR] {type(e).__name__}: {e}")
            self.app._set_status(f"✘ 폰트 적용 실패: {e}")

    # ------------------------------------------------------------ 현재 문단 → 빈줄 크기
    def _run_paragraph_size_8(self) -> None:
        """현재 캐럿이 위치한 문단 전체를 var_blank_size pt 로. 백그라운드."""
        size_str = self.var_blank_size.get().strip()
        self.app._set_status(f"[STAGE 3] 현재 문단 → {size_str}pt 적용 중...")
        threading.Thread(
            target=self._run_paragraph_size_8_async, daemon=True,
        ).start()

    def _run_paragraph_size_8_async(self) -> None:
        from tkinter import messagebox
        from forge.com_helpers import set_param

        # var_blank_size 의 현재 값 — UI 스레드 변수지만 read-only 라 안전
        size_str = self.var_blank_size.get().strip()

        self._log("")
        self._log(f"━━━━━━ 현재 문단 → 글자크기 {size_str}pt 시작 ━━━━━━")

        # 입력 검증
        try:
            size_pt = float(size_str)
            if size_pt <= 0:
                raise ValueError("크기는 양수여야 함")
        except (ValueError, TypeError):
            self._log(f"[ERR] 크기 파싱 실패: {size_str!r}")
            self.app._set_status(f"✘ 크기 입력 오류: {size_str!r}")
            return

        try:
            init_com_for_thread()
            try:
                session = self.app.ensure_hwp()
                self._log(f"[hwp] session: {session.version_name} #{session.instance_index}")
            except MultipleHwpInstancesError as e:
                self._log(f"[hwp] 다중 인스턴스 — picker 표시")
                self.app._set_status(f"⚠ {size_str}pt 적용 보류: 한/글 인스턴스 선택 필요")
                self.app.prompt_pick_from_worker(e.instances)
                return
            except NoExistingHwpError as e:
                self._log(f"[hwp] 한/글 미실행: {e}")
                self.app._set_status(f"✘ {size_str}pt 적용 실패: 한/글 미실행")
                self.app.root.after(0, lambda: messagebox.showwarning(
                    "한/글 미실행",
                    "떠 있는 한/글 인스턴스가 없습니다.\n"
                    "한/글을 먼저 실행하시고 '한/글 선택' 버튼으로 연결해 주세요.",
                ))
                return
            except Exception as e:
                self._log(f"[hwp] attach 실패: {e}")
                self.app._set_status(f"✘ {size_str}pt 적용 실패: {e}")
                return

            hwp = session.hwp
            # 현재 문단 전체 selection — face 보존, Height 만 변경
            hwp.Run("MoveParaBegin")
            hwp.Run("MoveSelParaEnd")
            height_units = int(size_pt * 100)
            set_param(hwp, "CharShape", {"Height": height_units})
            hwp.Run("Cancel")

            self._log(f"[ok] CharShape Height={height_units} ({size_pt}pt) 적용")
            self._log(f"━━━━━━ 현재 문단 → 글자크기 {size_str}pt 완료 ━━━━━━")
            done_msg = f"✔ 현재 문단 → 글자크기 {size_str}pt 적용 완료"
            self._log(done_msg)
            self.app._set_status(done_msg)
        except Exception as e:
            self._log(f"[ERROR] {type(e).__name__}: {e}")
            self.app._set_status(f"✘ {size_str}pt 적용 실패: {e}")

    # ------------------------------------------------------------ 자간 0 초기화
    def _run_kerning_reset(self) -> None:
        """선택 영역 또는 현재 문단의 자간을 0 으로. 백그라운드."""
        self.app._set_status("[STAGE 3] 자간 0 초기화 중...")
        threading.Thread(
            target=self._run_kerning_reset_async, daemon=True,
        ).start()

    def _run_kerning_reset_async(self) -> None:
        from tkinter import messagebox
        from forge.com_helpers import set_param
        from forge.linter._range import selection_range

        self._log("")
        self._log("━━━━━━ 자간 0 초기화 시작 ━━━━━━")
        try:
            init_com_for_thread()
            try:
                session = self.app.ensure_hwp()
                self._log(f"[hwp] session: {session.version_name} #{session.instance_index}")
            except MultipleHwpInstancesError as e:
                self._log(f"[hwp] 다중 인스턴스 — picker 표시")
                self.app._set_status("⚠ 자간 reset 보류: 한/글 인스턴스 선택 필요")
                self.app.prompt_pick_from_worker(e.instances)
                return
            except NoExistingHwpError as e:
                self._log(f"[hwp] 한/글 미실행: {e}")
                self.app._set_status("✘ 자간 reset 실패: 한/글 미실행")
                self.app.root.after(0, lambda: messagebox.showwarning(
                    "한/글 미실행",
                    "떠 있는 한/글 인스턴스가 없습니다.\n"
                    "한/글을 먼저 실행하시고 '한/글 선택' 버튼으로 연결해 주세요.",
                ))
                return
            except Exception as e:
                self._log(f"[hwp] attach 실패: {e}")
                self.app._set_status(f"✘ 자간 reset 실패: {e}")
                return

            hwp = session.hwp
            # selection 있으면 그대로, 없으면 현재 문단 전체 select
            sel = selection_range(hwp)
            if sel is None:
                self._log("  [scope] selection 없음 → 현재 문단 전체 select")
                hwp.Run("MoveParaBegin")
                hwp.Run("MoveSelParaEnd")
                cancel_after = True
            else:
                self._log(f"  [scope] selection {sel[0]} → {sel[1]}")
                cancel_after = False

            # 한/글 7개 언어 면 Spacing 모두 0 (tool2 `글자간격(0)` 패턴)
            set_param(hwp, "CharShape", {
                "SpacingHangul":   0,
                "SpacingLatin":    0,
                "SpacingHanja":    0,
                "SpacingJapanese": 0,
                "SpacingUser":     0,
                "SpacingSymbol":   0,
                "SpacingOther":    0,
            })

            if cancel_after:
                hwp.Run("Cancel")

            self._log("[ok] 7-언어면 Spacing* = 0 적용")
            self._log("━━━━━━ 자간 0 초기화 완료 ━━━━━━")
            done_msg = "✔ 자간 0 초기화 완료"
            self._log(done_msg)
            self.app._set_status(done_msg)
        except Exception as e:
            self._log(f"[ERROR] {type(e).__name__}: {e}")
            self.app._set_status(f"✘ 자간 reset 실패: {e}")

    # ---------------------------- 선택 영역 → 마크다운 변환 (Ctrl+Shift+X)
    def _run_md_convert_selection(self) -> None:
        """한/글 선택 영역의 plain text 를 md 로 해석해 그 자리에 변환 출력. 백그라운드."""
        self.app._set_status("[md-convert] 선택 영역 변환 중...")
        threading.Thread(
            target=self._run_md_convert_selection_async, daemon=True,
        ).start()

    def _run_md_convert_selection_async(self) -> None:
        from tkinter import messagebox

        self._log("")
        self._log("━━━━━━ 선택 영역 → 마크다운 변환 시작 ━━━━━━")
        try:
            init_com_for_thread()
            try:
                session = self.app.ensure_hwp()
                self._log(f"[hwp] session: {session.version_name} #{session.instance_index}")
            except MultipleHwpInstancesError as e:
                self._log(f"[hwp] 다중 인스턴스 ({len(e.instances)}개) — picker 표시")
                self.app._set_status("⚠ md 변환 보류: 한/글 인스턴스 선택 필요")
                self.app.prompt_pick_from_worker(e.instances)
                return
            except NoExistingHwpError as e:
                self._log(f"[hwp] 한/글 미실행: {e}")
                self.app._set_status("✘ md 변환 실패: 한/글 미실행")
                self.app.root.after(0, lambda: messagebox.showwarning(
                    "한/글 미실행",
                    "떠 있는 한/글 인스턴스가 없습니다.\n"
                    "한/글을 먼저 실행하시고 '한/글 선택' 버튼으로 연결해 주세요.",
                ))
                return
            except Exception as e:
                self._log(f"[hwp] attach 실패: {e}")
                self.app._set_status(f"✘ md 변환 실패: {e}")
                return

            try:
                node_count = convert_selection_to_hwpx(
                    session.hwp, spec=self.state.spec, log=self._log,
                )
            except NoSelectionError as e:
                self._log(f"[skip] {e}")
                self.app._set_status(f"⚠ md 변환 — {e}")
                self.app.root.after(0, lambda: messagebox.showinfo(
                    "선택 영역 없음",
                    "한/글에서 변환할 마크다운 본문을 영역으로 먼저 지정한 뒤 "
                    "다시 단축키를 눌러주세요 (단순 캐럿 위치만으로는 동작하지 않습니다).",
                ))
                return

            self._log("━━━━━━ 선택 영역 → 마크다운 변환 완료 ━━━━━━")
            done_msg = f"✔ 선택 영역 → md 변환 완료 ({node_count} 노드)"
            self._log(done_msg)
            self.app._set_status(done_msg)
        except Exception as e:
            self._log(f"[ERROR] {type(e).__name__}: {e}")
            import traceback
            self._log(traceback.format_exc())
            self.app._set_status(f"✘ md 변환 실패: {e}")

    # ---------------------------- 연속 실시 (들여쓰기 → 자간 → 들여쓰기)
    def _run_kerning_then_indent(self) -> None:
        """
        들여쓰기 → 자간 → 들여쓰기 3단계 연속 실시.

        순서 근거 (사용자 검증):
          - 자간조정의 `front`/`back` 계산은 현재 line wrap 위치를 기준으로 동작.
            wrap 위치는 left indent 값에 좌우됨.
          - 인덴트 0 상태에서 자간 → 인덴트 적용하면 wrap 점이 옆으로 밀려
            자간 보정값이 무의미해짐 (자간 효과 죽음).
          - 인덴트 → 자간 만 하면 자간 후 본문 위치 미세 drift 로 인덴트가
            살짝 어긋남.
          - 1차 인덴트로 wrap 기준 고정 → 자간 보정 → 2차 인덴트로 drift
            보정 — 두 인덴트는 역할이 다른 별개 호출.
        """
        self.app._set_status("[STAGE 3] 들여쓰기 → 자간 → 들여쓰기 적용 중...")
        threading.Thread(target=self._run_combined_async, daemon=True).start()

    def _run_combined_async(self) -> None:
        from tkinter import messagebox
        from forge.linter._range import apply_per_paragraph
        from forge.linter.indent_align import _process_paragraph
        from forge.linter.kerning import _adjust_paragraph

        self._log("")
        self._log("━━━━━━ 들여쓰기 → 자간 → 들여쓰기 (연속) 시작 ━━━━━━")
        try:
            init_com_for_thread()
            try:
                # ★ ensure_hwp — 살아있는 세션 재사용 (Visible=True 재호출 회피).
                session = self.app.ensure_hwp()
                self._log(f"[hwp] session: {session.version_name} #{session.instance_index}")
            except MultipleHwpInstancesError as e:
                self._log(f"[hwp] 다중 인스턴스 ({len(e.instances)}개) — picker 표시")
                self.app._set_status("⚠ 자간→들여쓰기 보류: 한/글 인스턴스 선택 필요")
                self.app.prompt_pick_from_worker(e.instances)
                return
            except NoExistingHwpError as e:
                self._log(f"[hwp] 한/글 미실행: {e}")
                self.app._set_status("✘ 자간→들여쓰기 실패: 한/글 미실행")
                self.app.root.after(0, lambda: messagebox.showwarning(
                    "한/글 미실행",
                    "떠 있는 한/글 인스턴스가 없습니다.\n"
                    "한/글을 먼저 실행하시고 '한/글 선택' 버튼으로 연결해 주세요.",
                ))
                return
            except Exception as e:
                self._log(f"[hwp] attach 실패: {e}")
                self.app._set_status("✘ 자간→들여쓰기 실패: 한/글 attach 불가")
                self.app.root.after(0, lambda: messagebox.showerror(
                    "한/글 연결 실패",
                    f"한/글 인스턴스 연결 중 오류가 발생했습니다.\n\n세부: {e}",
                ))
                return
            hwp = session.hwp

            # 한 문단에 들여쓰기 → 자간 → 들여쓰기 3 단계 순차 적용.
            # apply_per_paragraph 가 selection 검사 후 범위 내 모든 문단에 이 fn 호출.
            def _combined_one_paragraph(h, log):
                # 매 단계 종료 후 캐럿이 다음 문단/줄로 이동할 수 있어 매번 시작
                # 위치로 복귀해야 함 — 시작 pos 를 기록해 두고 SetPos 로 복원.
                h.Run("MoveParaBegin")
                start_pos = h.GetPos()
                log(f"  [combined] 문단 시작 pos={start_pos!r}")

                def _restore(stage_label: str):
                    try:
                        h.SetPos(*start_pos)
                        log(f"  [restore→{stage_label}] SetPos → {start_pos!r}")
                    except Exception as e:
                        log(f"  [restore→{stage_label}] 복귀 실패 ({e}) — MoveParaBegin fallback")
                        h.Run("MoveParaBegin")

                # 1) 1차 들여쓰기 — line wrap 기준 확정 (이게 없으면 자간 효과 죽음)
                log("  --- 1단계: 들여쓰기 정렬 (wrap 기준 확정) ---")
                _process_paragraph(h, log)

                # 2) 자간조정 — 확정된 wrap 위에서 어절 잘림 보정
                _restore("자간")
                log("  --- 2단계: 자간조정 ---")
                _adjust_paragraph(h, log)

                # 3) 2차 들여쓰기 — 자간 보정으로 본문 위치 drift 발생 가능, 재정렬
                _restore("재정렬")
                log("  --- 3단계: 들여쓰기 재정렬 (drift 보정) ---")
                _process_paragraph(h, log)

            apply_per_paragraph(hwp, _combined_one_paragraph, self._log)

            self._log("━━━━━━ 들여쓰기 → 자간 → 들여쓰기 (연속) 완료 ━━━━━━")
            done_msg = "✔ 들여쓰기·자간·재정렬 (연속) 완료"
            self._log(done_msg)
            self.app._set_status(done_msg)
        except Exception as e:
            self._log(f"[ERROR] {type(e).__name__}: {e}")
            self.app._set_status(f"✘ 연속 처리 실패: {e}")

    # ------------------------------------------------------------ 미구현 핸들러
    def _not_implemented(self, name: str) -> None:
        from tkinter import messagebox
        messagebox.showinfo(
            "향후 구현",
            f"'{name}' 는 다음 단계에서 forge.rules.polisher 에 구현 예정.",
        )

    def on_hwp_ready(self) -> None:
        pass
