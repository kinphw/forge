"""
보고서 양식 삽입 — 고정 8 종 (헤더 3 / 참고박스 4 / 붙임박스 1).

각 함수는 활성 한/글 문서의 '현재 커서 위치' 에 양식을 emit. UI 의 양식삽입
탭에서 버튼으로 호출. placeholder 글자(`◆◆◆`, `◎◎◎` 등) 는 사용자가
직접 교체할 자리로 남겨둠.
"""
from __future__ import annotations

from datetime import date
from typing import Any, Callable, Optional

from ..renderers import primitives as p


# ============================================================================
# 1. 메타헤더 (대제목 + 부서·일자 stamp) — md 변환의 MetadataRenderer 재활용
# ============================================================================

def 메타헤더(hwp: Any) -> None:
    """제목 노란 박스 + 부서·일자 stamp — md 변환의 MetadataRenderer 재호출.

    샘플 값으로 emit (사용자가 한/글에서 텍스트만 교체):
      - 보고서명: '◆◆◆◆◆ 진행상황 및 대응방안'
      - 작성부서: '◎◎◎◎◎◎국 ◇◇◇◇팀'
      - 작성일: 오늘
    """
    from ..formatter.templates import REPORT1_SPEC
    from ..renderers.metadata import MetadataRenderer
    spec = REPORT1_SPEC.clone()
    MetadataRenderer(hwp, spec).render(
        보고서명="◆◆◆◆◆ 진행상황 및 대응방안",
        작성부서="◎◎◎◎◎◎국 ◇◇◇◇팀",
        작성일=date.today(),
    )


# ============================================================================
# 2~6. 금감원페이지 family (5) — Forge 가 spec 기준으로 삼는 양식
# ============================================================================

def 금감원페이지중제목(hwp: Any, 숫자: str = "Ⅰ. ", 내용: str = "◆◆◆◆◆ 진행상황") -> None:
    """파란 underline 중제목 — HY견명조 15pt Bold 숫자 + HY헤드라인M 16pt 본문."""
    pos = hwp.GetPosBySet()
    try:
        if pos.Item("Pos") != 0:
            hwp.HAction.Run("BreakPara")
    except Exception:
        pass
    p.set_font_size(hwp, 8)
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "justify")
    p.make_table(hwp, [205 - p.measure_para_margin_mm(hwp)], [8.4])
    p.set_cell_margin_zero(hwp)
    p.set_table_border_type(hwp, 0, 1, 0, 0)
    p.set_table_border_thickness(hwp, 6, 8, 6, 6)
    p.set_table_border_color(hwp, 0, 0, 255)
    p.set_font(hwp, "HY견명조", 15)
    hwp.HAction.Run("CharShapeBold")
    p.insert_text(hwp, 숫자)
    hwp.HAction.Run("CharShapeNormal")
    p.set_font(hwp, "HY헤드라인M", 16)
    p.insert_text(hwp, 내용)
    hwp.HAction.Run("MoveRight")
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "justify")


def 금감원페이지소제목(hwp: Any, 번호: str = "가", 내용: str = "개요") -> None:
    """라벤더 마커+본문 2셀 소제목 — HY헤드라인M 15/15.5pt."""
    pos = hwp.GetPosBySet()
    try:
        if pos.Item("Pos") != 0:
            hwp.HAction.Run("BreakPara")
    except Exception:
        pass
    p.set_font_size(hwp, 8)
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "justify")
    p.make_table(hwp, [7.5, 1, 49], [8.7])
    p.set_table_border_color(hwp, 62, 87, 165)
    p.set_table_bg(hwp, 224, 229, 250)
    p.set_table_border_thickness(hwp, 6, 6, 6, 6)
    p.set_font(hwp, "HY헤드라인M", 15)
    p.align(hwp, "center")
    p.insert_text(hwp, 번호)
    hwp.HAction.Run("TableRightCellAppend")
    p.set_table_border_type(hwp, 0, 0, 1, 1)
    hwp.HAction.Run("TableRightCellAppend")
    p.set_table_border_color(hwp, 62, 87, 165)
    p.set_table_border_thickness(hwp, 6, 6, 6, 6)
    p.set_font(hwp, "HY헤드라인M", 15.5)
    p.insert_text(hwp, 내용)
    hwp.HAction.Run("MoveRight")
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "justify")


def 금감원페이지꺽쇠박스(hwp: Any) -> None:
    """`〈 ◈◈◈◈ 관련 현황 〉` 3셀 꺽쇠 박스 — 맑은 고딕 13pt."""
    pos = hwp.GetPosBySet()
    try:
        if pos.Item("Pos") != 0:
            hwp.HAction.Run("BreakPara")
    except Exception:
        pass
    p.set_font_size(hwp, 8)
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "right")
    p.make_table(hwp, [35, 83, 35], [2, 2, 22])
    hwp.HAction.Run("TableCellBlock")
    hwp.HAction.Run("TableCellBlockExtend")
    hwp.HAction.Run("TableCellBlockExtend")
    p.set_table_border_type(hwp, 0, 0, 0, 0)
    p.set_table_inner_line_type(hwp, 0, 0)
    p.set_font_size(hwp, 3)
    hwp.HAction.Run("Cancel")
    hwp.MovePos(106)
    hwp.MovePos(104)
    p.move_table_right(hwp, 1)
    hwp.HAction.Run("TableCellBlock")
    hwp.HAction.Run("TableCellBlockExtend")
    hwp.MovePos(103)
    hwp.HAction.Run("TableMergeCell")
    p.align(hwp, "center")
    p.set_font(hwp, "맑은 고딕", 13)
    hwp.HAction.Run("CharShapeBold")
    p.insert_text(hwp, "〈")
    hwp.HAction.Run("InsertFixedWidthSpace")
    p.insert_text(hwp, "◈◈◈◈ 관련 현황")
    hwp.HAction.Run("InsertFixedWidthSpace")
    p.insert_text(hwp, "〉")
    p.move_table_right(hwp, 2)
    p.set_table_border_single_line(hwp, "상", 1, 1)
    p.set_table_border_single_line(hwp, "좌", 1, 1)
    p.move_table_right(hwp, 2)
    p.set_table_border_single_line(hwp, "상", 1, 1)
    p.set_table_border_single_line(hwp, "우", 1, 1)
    p.move_table_right(hwp, 1)
    hwp.HAction.Run("TableCellBlock")
    hwp.HAction.Run("TableCellBlockExtend")
    p.move_table_right(hwp, 2)
    hwp.HAction.Run("TableMergeCell")
    p.set_table_border_single_line(hwp, "좌", 1, 1)
    p.set_table_border_single_line(hwp, "우", 1, 1)
    p.set_table_border_single_line(hwp, "하", 1, 1)
    p.set_font(hwp, "맑은 고딕", 13)
    hwp.HAction.Run("InsertFixedWidthSpace")
    p.insert_text(hwp, "※ 맑은고딕 13pt")
    hwp.HAction.Run("BreakPara")
    p.set_font_size(hwp, 4)
    hwp.HAction.Run("BreakPara")
    p.set_font_size(hwp, 13)
    hwp.HAction.Run("InsertFixedWidthSpace")
    hwp.HAction.Run("InsertFixedWidthSpace")
    p.insert_text(hwp, "◦ 맑은고딕 13pt")
    hwp.HAction.Run("BreakPara")
    p.set_font_size(hwp, 3)
    hwp.HAction.Run("BreakPara")
    p.set_font_size(hwp, 11)
    for _ in range(7):
        hwp.HAction.Run("InsertFixedWidthSpace")
    p.insert_text(hwp, "* 맑은고딕 11pt")
    hwp.HAction.Run("MoveRight")
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "justify")


def 금감원페이지점선박스(hwp: Any) -> None:
    """`⇨` 결론 점선 박스 — 휴먼명조 15pt 민트 배경."""
    pos = hwp.GetPosBySet()
    try:
        if pos.Item("Pos") != 0:
            hwp.HAction.Run("BreakPara")
    except Exception:
        pass
    p.set_font_size(hwp, 8)
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "right")
    p.make_table(hwp, [199.5 - p.measure_para_margin_mm(hwp)], [18])
    p.set_table_border_type(hwp, 3, 3, 3, 3)
    p.set_table_border_thickness(hwp, 2, 2, 2, 2)
    p.set_table_bg(hwp, 205, 242, 228)
    p.set_font(hwp, "휴먼명조", 15)
    p.insert_text(hwp, "⇨ 휴먼명조 15pt")
    hwp.HAction.Run("MoveRight")
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "justify")


def 금감원페이지참고(hwp: Any) -> None:
    """[참고] 라벨 + 본문 2셀 callout — 진파 라벨 / HY헤드라인M 15pt 본문."""
    p.make_table(hwp, [17.6, 1, 182 - p.measure_para_margin_mm(hwp)], [8.7])
    p.set_cell_margin_zero(hwp)
    p.set_table_bg(hwp, 0, 0, 255)
    hwp.HAction.Run("CharShapeBold")
    p.set_font(hwp, "HY헤드라인M", 15)
    p.set_text_color(hwp, 255, 255, 255)
    p.align(hwp, "center")
    p.insert_text(hwp, "참고")
    p.move_table_right(hwp, 1)
    p.set_table_border_type(hwp, 0, 0, 1, 1)
    hwp.HAction.Run("TableResizeExLeft")
    hwp.HAction.Run("TableResizeExLeft")
    p.move_table_right(hwp, 1)
    p.set_font(hwp, "HY헤드라인M", 15)
    hwp.HAction.Run("InsertFixedWidthSpace")
    hwp.HAction.Run("InsertFixedWidthSpace")
    p.insert_text(hwp, "HY헤드라인M 15pt")
    hwp.HAction.Run("MoveRight")
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "justify")


# ============================================================================
# 7. 마크다운 [참고] 박스 — md 변환과 동일한 NoteCalloutRenderer 호출
# ============================================================================

def 참고박스_마크다운(hwp: Any) -> None:
    """마크다운 `[참고]` callout 과 동일 양식 — 점선 박스 + `※ (참고) 본문`.

    md 변환의 NoteCalloutRenderer 그대로 재호출. 샘플 2 줄:
      'ㅇㅇㅇ' / 'ㅁㅁㅁ'
    """
    from ..formatter.templates import REPORT1_SPEC
    from ..renderers.note_callout import NoteCalloutRenderer
    spec = REPORT1_SPEC.clone()
    NoteCalloutRenderer(hwp, spec).render(["ㅇㅇㅇ", "ㅁㅁㅁ"])


# ============================================================================
# 8. 금감보고서 family (1) — 진남 헤더 callout 변형
# ============================================================================

def 금감보고서블루진박스(hwp: Any) -> None:
    """진남색 헤더+본문 3셀 callout — 맑은고딕 12pt Bold 헤더."""
    pos = hwp.GetPosBySet()
    try:
        if pos.Item("Pos") != 0:
            hwp.HAction.Run("BreakPara")
    except Exception:
        pass
    p.set_font_size(hwp, 10)
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "right")
    p.make_table(hwp, [47, 59, 47], [2.7, 2.7, 36])
    hwp.HAction.Run("TableCellBlock")
    hwp.HAction.Run("TableCellBlockExtend")
    hwp.HAction.Run("TableCellBlockExtend")
    p.set_table_border_type(hwp, 0, 0, 0, 0)
    p.set_table_inner_line_type(hwp, 0, 0)
    p.set_font_size(hwp, 3)
    hwp.HAction.Run("Cancel")
    hwp.MovePos(106)
    hwp.MovePos(104)
    p.move_table_right(hwp, 1)
    hwp.HAction.Run("TableCellBlock")
    hwp.HAction.Run("TableCellBlockExtend")
    hwp.MovePos(103)
    hwp.HAction.Run("TableMergeCell")
    p.set_table_bg(hwp, 58, 60, 132)
    p.set_text_color(hwp, 255, 255, 255)
    p.align(hwp, "center")
    p.set_font(hwp, "맑은 고딕", 12)
    hwp.HAction.Run("CharShapeBold")
    p.insert_text(hwp, "맑은고딕 12pt")
    p.move_table_right(hwp, 2)
    p.set_table_border_single_line(hwp, "상", 1, 1)
    p.set_table_border_single_line(hwp, "좌", 1, 1)
    p.move_table_right(hwp, 2)
    p.set_table_border_single_line(hwp, "상", 1, 1)
    p.set_table_border_single_line(hwp, "우", 1, 1)
    p.move_table_right(hwp, 1)
    hwp.HAction.Run("TableCellBlock")
    hwp.HAction.Run("TableCellBlockExtend")
    p.move_table_right(hwp, 2)
    hwp.HAction.Run("TableMergeCell")
    p.set_table_border_single_line(hwp, "좌", 1, 1)
    p.set_table_border_single_line(hwp, "우", 1, 1)
    p.set_table_border_single_line(hwp, "하", 1, 1)
    p.set_font(hwp, "맑은 고딕", 13)
    p.insert_text(hwp, "▣ 맑은고딕 13pt")
    p.set_cell_vertical_align(hwp, 0)
    hwp.HAction.Run("MoveRight")
    hwp.HAction.Run("BreakPara")
    p.align(hwp, "justify")


# ============================================================================
# 카탈로그 — templates_tab 이 import
# ============================================================================

# (번호, group, 함수, 라벨, 설명, image_count)
# group: 헤더 / 참고박스 / 붙임박스 — UI 가 이 키 변경 지점마다 separator 삽입
금감_TEMPLATES: list[tuple[int, str, Callable, str, str, int]] = [
    # ── 헤더 ──
    (1, "헤더",     메타헤더,                "메타헤더 (제목+팀+일자)",   "노란박스 + 부서·일자 stamp (샘플값)", 0),
    (2, "헤더",     금감원페이지중제목,      "중제목 (Ⅰ./Ⅱ.)",            "파란밑줄 HY견명조+HY헤드라인M", 0),
    (3, "헤더",     금감원페이지소제목,      "소제목 (가./나.)",           "라벤더 마커+본문 2셀", 0),
    # ── 참고박스 ──
    (4, "참고박스", 금감원페이지꺽쇠박스,    "꺽쇠박스",                   "〈◈◈◈ 관련 현황〉 3셀 헤더", 0),
    (5, "참고박스", 금감원페이지점선박스,    "점선박스",                   "⇨ 결론 점선박스 민트", 0),
    (6, "참고박스", 참고박스_마크다운,        "참고박스 (마크다운 변환)",   "md `[참고]` 점선박스 + ※(참고) 13pt", 0),
    (7, "참고박스", 금감보고서블루진박스,    "블루진박스",                 "진남 헤더 + 12pt Bold + 본문", 0),
    # ── 붙임박스 ──
    (8, "붙임박스", 금감원페이지참고,        "참고 (진파헤더)",            "진파 라벨 + HY헤드라인M 15pt 본문", 0),
]
