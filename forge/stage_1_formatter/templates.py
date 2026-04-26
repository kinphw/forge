"""
보고서 템플릿 spec — 기본값.

출처: reference/tool2/보고서1_spec.md (= tool2 금감원페이지 정확 spec)
용도: GUI 탭 1에서 사용자가 보는 기본값. 사용자가 테일러링하면 dataclass
      를 복사·수정해서 넘긴다.

지금은 "보고서 1" (= 금감원페이지) 1종만 정의. 후속 작업으로 다른 4종
(금감보고서·금감업무정보·금감보도자료·금감원장보고) 추가 예정.
"""
from __future__ import annotations

from dataclasses import dataclass, field, replace
from typing import Optional


@dataclass
class PageMargins:
    """문서 여백 (mm). 사용자 요청: 좌우 20, 나머지 모두 10."""
    left: float = 20.0
    right: float = 20.0
    top: float = 10.0
    bottom: float = 10.0
    header: float = 10.0
    footer: float = 10.0


@dataclass
class BulletStyle:
    """글머리 1단계 spec — tool2 금감원글머리지정 11속성과 1:1 대응."""
    md_glyph: str               # md 입력 글머리 (□ ○ - ·)
    out_glyph: str              # 출력 글머리 (□ ◦ * †)
    font: str                   # 폰트명 (휴먼명조, 맑은 고딕 등)
    size_pt: float              # 글자 크기 pt
    indent_pt: float            # 내어쓰기 (음수 가능)
    bold: bool = False
    space_above_pt: float = 0.0 # 위 빈 줄 크기 (pt)
    line_spacing: int = 150     # 줄간격 %
    fixed_pre: int = 0          # 글머리 앞 InsertFixedWidthSpace 횟수
    fixed_post: int = 0         # 글머리 뒤 InsertFixedWidthSpace 횟수


@dataclass
class ReportSpec:
    """전체 보고서 양식 spec — 사용자가 GUI에서 테일러링 가능."""
    name: str = "보고서 1 (금감원페이지)"
    code: str = "report1"

    # 페이지 설정
    margins: PageMargins = field(default_factory=PageMargins)
    line_spacing_default: int = 150     # 본문·제목·stamp 모두 일괄 150%

    # 대제목 (노란 박스)
    title_font: str = "HY헤드라인M"
    title_size_pt: float = 17.0
    title_bg_rgb: tuple[int, int, int] = (250, 250, 191)  # 연노랑
    title_box_height_mm: float = 10.5
    title_border_thickness: int = 6

    # 부서·일자 stamp (우정렬)
    date_font: str = "휴먼명조"
    date_size_pt: float = 12.0

    # 중제목 (Ⅰ./Ⅱ.)
    section_number_font: str = "HY견명조"
    section_number_size_pt: float = 15.0
    section_number_bold: bool = True
    section_title_font: str = "HY헤드라인M"
    section_title_size_pt: float = 16.0
    section_underline_rgb: tuple[int, int, int] = (0, 0, 255)  # 파란
    section_box_height_mm: float = 8.4

    # 소제목 (가/나/[1]/[2])
    subsection_font: str = "HY헤드라인M"
    subsection_marker_size_pt: float = 15.0
    subsection_content_size_pt: float = 15.5
    subsection_marker_bg_rgb: tuple[int, int, int] = (224, 229, 250)  # 라벤더
    subsection_border_rgb: tuple[int, int, int] = (62, 87, 165)       # 진파랑
    subsection_box_height_mm: float = 8.7
    subsection_marker_width_mm: float = 7.5
    subsection_content_width_mm: float = 49.0

    # ─── 본문 글머리 4단계 (□ ○ - ·) ───
    # 사용자 명시: 모두 휴먼명조 15pt 동일, 깊이만 균등 누진.
    #   - 내어쓰기: -22 → -33.6 (Δ-11.6) → -45.2 → -56.8
    #   - fixed_pre: 1 → 3 → 5 → 7 (왼쪽 들여쓰기 2칸씩)
    bullets: list[BulletStyle] = field(default_factory=lambda: [
        BulletStyle(  # L1 □
            md_glyph="□", out_glyph="□",
            font="휴먼명조", size_pt=15.0, indent_pt=-22.0,
            bold=False, space_above_pt=10.0, line_spacing=150,
            fixed_pre=1, fixed_post=2,
        ),
        BulletStyle(  # L2 ○
            md_glyph="○", out_glyph="◦",
            font="휴먼명조", size_pt=15.0, indent_pt=-33.6,
            bold=False, space_above_pt=8.0, line_spacing=150,
            fixed_pre=3, fixed_post=2,
        ),
        BulletStyle(  # L3 -
            md_glyph="-", out_glyph="-",
            font="휴먼명조", size_pt=15.0, indent_pt=-45.2,
            bold=False, space_above_pt=6.0, line_spacing=150,
            fixed_pre=5, fixed_post=2,
        ),
        BulletStyle(  # L4 ·
            md_glyph="·", out_glyph="·",
            font="휴먼명조", size_pt=15.0, indent_pt=-56.8,
            bold=False, space_above_pt=4.0, line_spacing=150,
            fixed_pre=7, fixed_post=2,
        ),
    ])

    # ─── 주석 (단일 spec) ───
    # 사용자 명시: *, ※(당구장), †(십자가) 3종 모두 동일 처리.
    #   맑은 고딕 12pt — 별도 행 분리 불필요.
    # 출력 글리프는 입력 마커 그대로 보존 (md `*` → out `*`, md `※` → out `※`).
    annotation: BulletStyle = field(default_factory=lambda: BulletStyle(
        md_glyph="*",  # 대표 마커. 실제로는 * / ** / *** / ※ / † 모두 이 spec 적용
        out_glyph="",  # 입력 마커 그대로 — 빈 문자열은 "marker 보존" 의미
        font="맑은 고딕", size_pt=12.0, indent_pt=-33.6,
        bold=False, space_above_pt=3.0, line_spacing=150,
        fixed_pre=8, fixed_post=2,
    ))

    # 결론 화살표 박스 (=>)
    conclusion_font: str = "휴먼명조"
    conclusion_size_pt: float = 15.0
    conclusion_bg_rgb: tuple[int, int, int] = (205, 242, 228)  # 민트
    conclusion_box_height_mm: float = 18.0
    conclusion_border_dotted: bool = True

    # 참고 callout
    note_header_font: str = "HY헤드라인M"
    note_header_size_pt: float = 15.0
    note_header_bg_rgb: tuple[int, int, int] = (0, 0, 255)         # 진파랑
    note_header_text_rgb: tuple[int, int, int] = (255, 255, 255)   # 흰색
    note_header_width_mm: float = 17.6
    note_box_height_mm: float = 8.7

    def clone(self, **overrides) -> "ReportSpec":
        """일부 필드만 바꾼 사본 반환 (사용자 테일러링용)."""
        return replace(self, **overrides)


# ==========================================================================
# 표준 spec 인스턴스
# ==========================================================================

REPORT1_SPEC = ReportSpec()
"""
보고서 1 = 금감원페이지 표준 spec.
출처: reference/tool2/보고서1_spec.md (tool2 코드에서 직접 추출)
"""

# 향후 추가:
# REPORT2_SPEC = ReportSpec(name="보고서 2 (금감보고서)", ...)
# BUSINESS_INFO_SPEC = ReportSpec(name="업무정보", ...)
# PRESS_RELEASE_SPEC = ReportSpec(name="보도자료", ...)
