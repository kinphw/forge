"""
탭 ⓪ How to? — 사용 안내.

처음 사용하는 사용자가 어떤 탭에서 무엇을 할 수 있는지 한눈에 보고
바로 시작할 수 있도록 정리한 안내 페이지. 동적 동작 없음 (정적 텍스트).

배치 우선순위: 노트북 가장 앞. 사용자가 익숙해지면 탭 ① 로 이동해 작업.
"""
from __future__ import annotations

import tkinter as tk
from tkinter import ttk
from tkinter.constants import BOTH, W
from tkinter.ttk import LabelFrame as TtkLabelFrame

from ..scrolled import ScrolledFrame


# 사용자 학습용 텍스트 — 코드 분기 없는 순수 안내. 변경은 자유.
_INTRO = (
    "Forge 는 두 가지 사용 모드를 한 윈도우에서 제공합니다.\n"
    "  • 실시간 모드 — 한/글에서 작업 중인 문서에 룰 1개씩 즉시 적용 (탭 ①)\n"
    "  • 배치 모드 — 개조식 markdown 을 새 .hwpx 파일로 변환 (탭 ③)\n"
    "탭 ② 는 두 모드가 공통으로 쓰는 보고서 양식(폰트·여백·글머리)을 편집합니다."
)

_QUICKSTART = [
    ("1. 한/글을 먼저 실행",
     "빈 새 문서 또는 임의의 .hwp/.hwpx 를 열어둡니다. Forge 는 떠 있는 한/글에 attach 합니다."),
    ("2. 상단 '한/글 선택' 버튼 클릭 (Optional - 자동으로 수행됨)",
     "한/글이 여러 개 떠 있으면 어느 인스턴스에 작업할지 명시 선택. 1개뿐이면 자동 attach."),
    ("3. 탭 ① 또는 탭 ③ 에서 원하는 작업 시작",
     "버튼이나 단축키로 룰 적용. 결과는 한/글 화면에서 즉시 확인."),
]

_TAB1 = (
    "활성 한/글 문서에 정형 룰을 1 개씩 적용합니다.\n"
    "버튼을 직접 누르거나, 한/글 창에 포커스가 있어도 시스템 전역 단축키로 호출 가능.\n"
    "기능은 3 그룹으로 묶여 있습니다 (자세한 단축키 letter 는 탭 ① 화면 참고)."
)

# 화면이 너무 빽빽해진다는 사용자 피드백 (2026-05-08) 으로 단축키 9 행 일괄
# 노출은 폐기. 그룹명 + 한 줄 요약만 노출 — 정확한 letter 는 탭 ① 의 Entry 가
# SSOT (사용자가 변경 가능) 라 여기 중복 표기는 정보 노이즈만 됨.
_HOTKEY_GROUPS = [
    ("① 정렬·자간",     "자동 정렬, 어절 끌어올림"),
    ("② 폰트·크기",     "본문 / 주석 / 헤드라인 / 울릉도 / 빈줄 글자크기 — 5 종"),
    ("③ reset·변환",    "자간 0 초기화, 선택 영역 → 마크다운 변환"),
]

_HOTKEY_NOTE = (
    "각 단축키 letter 는 탭 ① 의 Entry 칸에서 자유롭게 변경 가능합니다.\n"
    "다른 앱이 같은 단축키를 잡고 있으면 등록 실패 — 상태바에 ⚠ 메시지로 표시됩니다."
)

_TAB2 = (
    "보고서 양식 spec 을 편집합니다 (대제목·중제목·소제목 폰트/크기, 본문 글머리 4단계, 페이지 여백 등).\n"
    "기본값은 금감원페이지 표준(REPORT1_SPEC). 여기서 바꾼 값은 탭 ③ 변환에 즉시 반영됩니다.\n"
    "문서 메타(보고서명·부서·일자)는 여기가 아니라 탭 ③ markdown front-matter 에서 지정합니다."
)

_TAB3 = (
    "좌측에 개조식 markdown 을 붙여넣고 '변환' 클릭 → 새 .hwpx 산출.\n"
    "Forge 는 GitHub style `#` 헤더가 아닌 '개조식' 입력 — 자세한 형식은 spec/markdown-spec.md 참조."
)

# ★ Forge 는 표준 markdown `#`/`##`/`###` 헤더를 쓰지 않음. 개조식 `1.`/`가.`/
#   글머리표 4 단계 + callout 의 7 종 의미만 인식. 대제목은 YAML front-matter
#   의 `보고서명` 키 (헤더 마크다운 아님).
_BULLETS = [
    ("보고서명: ...",      "대제목 — YAML front-matter (`---` 사이) 안의 키. 마크다운 `#` 헤더 X"),
    ("1.  2.  3. ...",     "섹션 헤더 (예: `1. 현황`) — 라인 시작 숫자+마침표"),
    ("가.  나.  다. ...",  "소제목 — 한글 가나다+마침표 (선택, 섹션 내 1~5개)"),
    ("□ / ○ / - / ·",      "본문 글머리 4 단계 (왼쪽이 상위)"),
    ("□ (요약) ...",       "□ 라인 마커 직후 (요약) — HY울릉도M 폰트 강조"),
    ("* / ** / ***",       "참조 주석 (별 개수로 단계)"),
    ("※ / †",              "일반 주석 (당구장 / 십자가)"),
    ("=> ...",             "결론 박스"),
    ("[참고]",             "참고 callout 박스 (다음 빈 줄까지 본문)"),
    ("[붙임] / [붙임 N]",  "붙임 — 자동 페이지 break 후 시작"),
    ("__강조__",           "인라인 Bold (markdown-spec v1.4)"),
]

_TIPS = [
    "한/글 인스턴스가 여러 개일 때 임의 선택을 막기 위해 picker 다이얼로그가 강제됩니다 — 사고 방지.",
    "DRM(Fasoo 등) 환경에서도 ShellExecute 우선으로 신규 spawn 하므로 정책 위반 없이 동작합니다.",
]


class HowToTab:
    def __init__(self, parent: tk.Misc):
        self.frame = ttk.Frame(parent)
        scrolled = ScrolledFrame(self.frame, padding=16)
        scrolled.pack(fill=BOTH, expand=True)
        c = scrolled.interior

        ttk.Label(
            c, text="How to? — Forge 사용 안내",
            font=("", 16, "bold"),
        ).pack(anchor=W, pady=(0, 8))

        ttk.Label(
            c, text=_INTRO, justify="left", wraplength=900,
        ).pack(anchor=W, pady=(0, 14))

        # 빠른 시작
        qs = TtkLabelFrame(c, text="빠른 시작", padding=12)
        qs.pack(fill="x", pady=(0, 12))
        for step, body in _QUICKSTART:
            row = ttk.Frame(qs)
            row.pack(fill="x", pady=2)
            ttk.Label(row, text=step, font=("", 10, "bold"), width=28, anchor=W).pack(side="left")
            ttk.Label(row, text=body, justify="left", wraplength=640).pack(side="left", anchor=W)

        # 탭 ① 안내
        t1 = TtkLabelFrame(c, text="탭 ① 개별 작업 — 실시간 모드", padding=12)
        t1.pack(fill="x", pady=(0, 12))
        ttk.Label(t1, text=_TAB1, justify="left", wraplength=860).pack(anchor=W, pady=(0, 8))

        hk = ttk.Frame(t1)
        hk.pack(fill="x")
        ttk.Label(hk, text="기능 그룹", font=("", 10, "bold")).pack(anchor=W, pady=(2, 4))
        for group, desc in _HOTKEY_GROUPS:
            row = ttk.Frame(hk)
            row.pack(fill="x", pady=1)
            ttk.Label(row, text=group, font=("", 10, "bold"),
                      width=18, anchor=W).pack(side="left")
            ttk.Label(row, text=desc, justify="left", wraplength=700).pack(side="left", anchor=W)
        ttk.Label(t1, text=_HOTKEY_NOTE, justify="left", wraplength=860,
                  foreground="#555").pack(anchor=W, pady=(8, 0))

        # 탭 ② 안내
        t2 = TtkLabelFrame(c, text="탭 ② 기본정보 — 양식 spec 편집", padding=12)
        t2.pack(fill="x", pady=(0, 12))
        ttk.Label(t2, text=_TAB2, justify="left", wraplength=860).pack(anchor=W)

        # 탭 ③ 안내
        t3 = TtkLabelFrame(c, text="탭 ③ 마크다운 입력 — 배치 변환", padding=12)
        t3.pack(fill="x", pady=(0, 12))
        ttk.Label(t3, text=_TAB3, justify="left", wraplength=860).pack(anchor=W, pady=(0, 8))
        for token, desc in _BULLETS:
            row = ttk.Frame(t3)
            row.pack(fill="x", pady=1)
            ttk.Label(row, text=token, font=("Consolas", 10), width=22, anchor=W).pack(side="left")
            ttk.Label(row, text=desc, justify="left", wraplength=640).pack(side="left", anchor=W)

        # 팁/주의
        tips = TtkLabelFrame(c, text="알아두면 좋은 점", padding=12)
        tips.pack(fill="x", pady=(0, 12))
        for t in _TIPS:
            ttk.Label(tips, text=f"• {t}", justify="left", wraplength=860).pack(anchor=W, pady=1)

        ttk.Label(
            c,
            text="자세한 정보는 우상단 '?' 버튼 (About) / README.md / spec/ 폴더 참조.",
            foreground="#666",
        ).pack(anchor=W, pady=(4, 0))
