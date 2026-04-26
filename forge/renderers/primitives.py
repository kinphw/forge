"""
공통 COM 헬퍼.

tool2의 `한컴라이브러리.기본한컴` 411 메서드 중 본 프로젝트가 자주 쓰는 30~40개만
함수 형태로 재구현. 모두 `set_param` 5단계 패턴 또는 `Run()` 단순 호출의 wrapper.

설계 원칙:
  - 함수 1개 = COM 액션 1~2개. 묶음 호출 안 함 (조합은 렌더러 책임)
  - 매개변수는 hwp 객체 + 의미 있는 값 (mm, pt, RGB 분리)
  - 단위 변환은 함수 안에서 처리 (호출자는 mm/pt 단위로만 생각)
"""
from __future__ import annotations

import re
from typing import Any, Literal

from ..com_helpers import set_param

Align = Literal["left", "center", "right", "justify"]
BorderType = Literal[0, 1, 3]   # 0=없음, 1=실선, 3=점선
BorderSide = Literal["상", "하", "좌", "우"]

# 인라인 Bold 토큰 — `__X__` (markdown-spec v1.4)
# 비탐욕 매칭 — `__a__b__` 의 경우 `a` 만 bold, `b` 는 plain
_BOLD_TOKEN_RE = re.compile(r"__(.+?)__")


# ============================================================================
# 단위 변환
# ============================================================================

def mm_to_hwp(hwp: Any, mm: float) -> int:
    """mm → HWP 단위."""
    return hwp.MiliToHwpUnit(mm)


def pt_to_hwp(hwp: Any, pt: float) -> int:
    """pt → HWP 단위. tool2 관례에 따라 *2 적용."""
    return hwp.PointToHwpUnit(pt * 2)


def rgb(hwp: Any, r: int, g: int, b: int) -> int:
    """(r,g,b) → HWP RGB."""
    return hwp.RGBColor(r, g, b)


# ============================================================================
# 단순 액션 실행
# ============================================================================

def run(hwp: Any, action: str) -> None:
    """매개변수 없는 액션 단순 실행."""
    hwp.HAction.Run(action)


def break_para(hwp: Any) -> None:
    """단락 break (BreakPara)."""
    hwp.HAction.Run("BreakPara")


def move_right(hwp: Any) -> None:
    """오른쪽 이동 (표 셀 탈출 등)."""
    hwp.HAction.Run("MoveRight")


def insert_fixed_space(hwp: Any, count: int = 1) -> None:
    """고정폭 공백 N회 삽입."""
    for _ in range(count):
        hwp.HAction.Run("InsertFixedWidthSpace")


# ============================================================================
# 텍스트 삽입
# ============================================================================

def insert_text(hwp: Any, text: str) -> None:
    """
    현재 위치에 텍스트 삽입 (tool2 '문장' 등가).

    인라인 `__X__` Bold 토큰 자동 처리. 토큰 사이 plain 텍스트와 bold
    텍스트를 번갈아 삽입하면서 매 bold 구간 전후로 CharShapeBold 토글.

    spec/markdown-spec.md v1.4: `__X__` 가 강조 표기. 이탤릭 미사용,
    asterisk 는 참조 전용. 비탐욕 매칭이라 중첩 `__a__b__` 는 `a` 만 bold.
    """
    if not text:
        return
    # split 결과: parts[0,2,4,...] = plain, parts[1,3,5,...] = bold
    parts = _BOLD_TOKEN_RE.split(text)
    for i, part in enumerate(parts):
        if not part:
            continue
        if i % 2 == 0:
            set_param(hwp, "InsertText", {"Text": part})
        else:
            hwp.HAction.Run("CharShapeBold")
            set_param(hwp, "InsertText", {"Text": part})
            hwp.HAction.Run("CharShapeBold")


# ============================================================================
# 정렬
# ============================================================================

def align(hwp: Any, mode: Align) -> None:
    """단락 정렬."""
    actions = {
        "left":    "ParagraphShapeAlignLeft",
        "center":  "ParagraphShapeAlignCenter",
        "right":   "ParagraphShapeAlignRight",
        "justify": "ParagraphShapeAlignJustify",
    }
    hwp.HAction.Run(actions[mode])


# ============================================================================
# 글자 모양
# ============================================================================

def set_font(hwp: Any, font: str, size_pt: float, bold: bool = False) -> None:
    """
    폰트·크기·Bold 일괄 적용. 한/글의 7개 언어 면 모두 지정.
    Bold 는 CharShape의 Bold 항목으로.
    """
    set_param(hwp, "CharShape", {
        "FaceNameUser":     font, "FontTypeUser":     1,
        "FaceNameHangul":   font, "FontTypeHangul":   1,
        "FaceNameSymbol":   font, "FontTypeSymbol":   1,
        "FaceNameOther":    font, "FontTypeOther":    1,
        "FaceNameJapanese": font, "FontTypeJapanese": 1,
        "FaceNameHanja":    font, "FontTypeHanja":    1,
        "FaceNameLatin":    font, "FontTypeLatin":    1,
        "Height": int(size_pt * 100),
        "Bold":   1 if bold else 0,
    })


def set_text_color(hwp: Any, r: int, g: int, b: int) -> None:
    """글자색 (CharShape.TextColor)."""
    set_param(hwp, "CharShape", {"TextColor": rgb(hwp, r, g, b)})


def char_bold_on(hwp: Any) -> None:
    """현재 위치부터 Bold 토글 ON (CharShapeBold 액션)."""
    hwp.HAction.Run("CharShapeBold")


def char_normal(hwp: Any) -> None:
    """글자 모양 기본으로 (CharShapeNormal)."""
    hwp.HAction.Run("CharShapeNormal")


# ============================================================================
# 문단 모양
# ============================================================================

def set_indent(hwp: Any, pt: float) -> None:
    """내어쓰기 (ParagraphShape.Indentation, pt 단위)."""
    set_param(hwp, "ParagraphShape", {
        "Indentation": pt_to_hwp(hwp, pt),
    })


def set_line_spacing(hwp: Any, pct: int) -> None:
    """줄간격 (% 단위)."""
    set_param(hwp, "ParagraphShape", {
        "LineSpacingType": 0,
        "LineSpacing": pct,
    })


def set_para_margin(hwp: Any, left_pt: float, right_pt: float) -> None:
    """문단 좌·우 여백."""
    set_param(hwp, "ParagraphShape", {
        "LeftMargin":  pt_to_hwp(hwp, left_pt),
        "RightMargin": pt_to_hwp(hwp, right_pt),
    })


# ============================================================================
# 페이지 설정
# ============================================================================

def set_page_margins(
    hwp: Any,
    left_mm: float, right_mm: float,
    top_mm: float, bottom_mm: float,
    header_mm: float, footer_mm: float,
) -> None:
    """문서 6방향 여백 적용.

    PageSetup 의 ParameterSet 은 SecDef 이고 그 안의 PageDef 는 중첩
    ParameterSet (PIT_SET) 이라 SetItem 에 점 경로를 넘길 수 없다.
    nested attribute 로 직접 대입해야 함 — tool2 `문서여백` 과 동일 패턴.
    """
    hwp.HAction.GetDefault("PageSetup", hwp.HParameterSet.HSecDef.HSet)
    page_def = hwp.HParameterSet.HSecDef.PageDef
    page_def.LeftMargin   = mm_to_hwp(hwp, left_mm)
    page_def.RightMargin  = mm_to_hwp(hwp, right_mm)
    page_def.TopMargin    = mm_to_hwp(hwp, top_mm)
    page_def.BottomMargin = mm_to_hwp(hwp, bottom_mm)
    page_def.HeaderLen    = mm_to_hwp(hwp, header_mm)
    page_def.FooterLen    = mm_to_hwp(hwp, footer_mm)
    hwp.HParameterSet.HSecDef.HSet.SetItem("ApplyTo", 3)  # 3 = 문서 전체
    hwp.HAction.Execute("PageSetup", hwp.HParameterSet.HSecDef.HSet)


# ============================================================================
# 표
# ============================================================================

def make_table(hwp: Any, cols_mm: list[float], rows_mm: list[float]) -> None:
    """
    표 생성.
    cols_mm: 각 열 폭 (mm) 리스트 → 길이가 열 수
    rows_mm: 각 행 높이 (mm) 리스트 → 길이가 행 수

    tool2 `표만들기(가로크기, 세로크기)` 와 동일 동작.
    표 생성 후 첫 셀에 커서 위치.
    """
    hwp.HAction.GetDefault("TableCreate", hwp.HParameterSet.HTableCreation.HSet)
    T = hwp.HParameterSet.HTableCreation
    T.Rows = len(rows_mm)
    T.Cols = len(cols_mm)
    T.WidthType = 2
    T.HeightType = 1
    T.CreateItemArray("ColWidth", len(cols_mm))
    for i, w in enumerate(cols_mm):
        T.ColWidth.SetItem(i, mm_to_hwp(hwp, w))
    T.CreateItemArray("RowHeight", len(rows_mm))
    for i, h in enumerate(rows_mm):
        T.RowHeight.SetItem(i, mm_to_hwp(hwp, h))
    T.TableProperties.TreatAsChar = 1
    hwp.HAction.Execute("TableCreate", hwp.HParameterSet.HTableCreation.HSet)


def set_cell_margin_zero(hwp: Any) -> None:
    """현재 표의 셀 4방향 여백을 0으로 + ShapeType=3, ShapeCellSize=0."""
    set_param(hwp, "TablePropertyDialog", {
        "CellMarginBottom": mm_to_hwp(hwp, 0.0),
        "CellMarginTop":    mm_to_hwp(hwp, 0.0),
        "CellMarginRight":  mm_to_hwp(hwp, 0.0),
        "CellMarginLeft":   mm_to_hwp(hwp, 0.0),
        "ShapeType":        3,
        "ShapeCellSize":    0,
    })


# 모든 셀·표 테두리 작업은 단일 액션 'CellBorderFill' 사용.
# (출처: tool2 한컴라이브러리 — 표테두리굵기/타입/색·표내부선*·표테두리단일선)

def set_table_border_type(hwp: Any, top: BorderType, bottom: BorderType,
                            left: BorderType, right: BorderType) -> None:
    """현재 표의 외곽 4방향 테두리 종류. 0=없음, 1=실선, 3=점선."""
    set_param(hwp, "CellBorderFill", {
        "BorderTypeTop":    int(top),
        "BorderTypeBottom": int(bottom),
        "BorderTypeLeft":   int(left),
        "BorderTypeRight":  int(right),
    })


def set_table_border_thickness(hwp: Any, top: int, bottom: int,
                                left: int, right: int) -> None:
    """현재 표의 외곽 4방향 테두리 굵기 (HWP 굵기 인덱스)."""
    set_param(hwp, "CellBorderFill", {
        "BorderWidthTop":    top,
        "BorderWidthBottom": bottom,
        "BorderWidthLeft":   left,
        "BorderWidthRight":  right,
    })


def set_table_border_color(hwp: Any, r: int, g: int, b: int) -> None:
    """현재 표의 외곽 테두리 색.
    주의: 한/글 COM API 의 좌측 항목명이 'BorderCorlorLeft' (Color 의 오타)."""
    color = rgb(hwp, r, g, b)
    set_param(hwp, "CellBorderFill", {
        "BorderColorTop":    color,
        "BorderColorBottom": color,
        "BorderColorRight":  color,
        "BorderCorlorLeft":  color,  # sic — 한컴 API 자체 오타
    })


def set_table_inner_line_type(hwp: Any, horizontal: BorderType,
                                vertical: BorderType) -> None:
    """현재 표의 내부선(가로·세로) 종류."""
    set_param(hwp, "CellBorderFill", {
        "TypeHorz": int(horizontal),
        "TypeVert": int(vertical),
    })


def set_table_inner_line_thickness(hwp: Any, horizontal: int, vertical: int) -> None:
    """현재 표의 내부선 굵기."""
    set_param(hwp, "CellBorderFill", {
        "WidthHorz": horizontal,
        "WidthVert": vertical,
    })


def set_table_inner_line_color(hwp: Any, r: int, g: int, b: int) -> None:
    """현재 표의 내부선 색."""
    color = rgb(hwp, r, g, b)
    set_param(hwp, "CellBorderFill", {
        "ColorHorz": color,
        "ColorVert": color,
    })


def set_table_bg(hwp: Any, r: int, g: int, b: int) -> None:
    """
    현재 셀의 배경색 — tool2 `표배경색` 1:1 재현.

    `CellBorderFill`(테두리 액션) 과 별개의 `CellFill` 액션 사용.
    set_param 5단계 패턴이 아니라 HParameterSet.HCellBorderFill.FillAttr 직접 설정.
    """
    color = hwp.RGBColor(r, g, b)
    hwp.HAction.GetDefault("CellFill", hwp.HParameterSet.HCellBorderFill.HSet)
    F = hwp.HParameterSet.HCellBorderFill.FillAttr
    F.type = hwp.BrushType("NullBrush|WinBrush")
    F.WinBrushFaceColor  = color
    F.WinBrushHatchColor = color
    F.WinBrushFaceStyle  = hwp.HatchStyle("None")
    F.WindowsBrush = 1
    hwp.HAction.Execute("CellFill", hwp.HParameterSet.HCellBorderFill.HSet)


def set_table_border_single_line(hwp: Any, side: BorderSide,
                                    width: int, line_type: int = 1) -> None:
    """
    단일 변에만 실선 적용. tool2 `표테두리단일선('상', 1, 1)` 등가.
    side: '상'/'하'/'좌'/'우'
    """
    suffix = {"상": "Top", "하": "Bottom", "좌": "Left", "우": "Right"}[side]
    set_param(hwp, "CellBorderFill", {
        f"BorderWidth{suffix}": width,
        f"BorderType{suffix}":  line_type,
    })


def move_table_right(hwp: Any, count: int = 1) -> None:
    """현재 표 안에서 오른쪽으로 N 셀 이동. tool2 `표오른쪽`."""
    for _ in range(count):
        hwp.HAction.Run("TableRightCellAppend")


# ============================================================================
# 위치
# ============================================================================

def get_current_pos(hwp: Any) -> Any:
    """현재 커서 위치 (PosBySet)."""
    return hwp.GetPosBySet()


def set_current_pos(hwp: Any, pos: Any) -> None:
    """커서를 주어진 위치로."""
    hwp.SetPosBySet(pos)


def is_at_line_start(hwp: Any) -> bool:
    """커서가 단락 시작인지 (Pos == 0)."""
    pos = hwp.GetPosBySet()
    try:
        return pos.Item("Pos") == 0
    except Exception:
        return True
