"""
TableRenderer — GFM 부분집합 표 (헤더 + 구분선 + 데이터 N행) → 한/글 표.

tool2 권위 reference:
  - `행안부초록표` (한컴라이브러리_decompiled.py:3053+) — 다행 N×M 표의 1:1
     패턴. 각 셀: [선택적 표배경색] → 문장풀(폰트,size,정렬,bold,텍스트) →
     TableRightCellAppend. 마지막에 표탈출().
  - `표만들기` (line 736-751)        — primitives.make_table 와 동일.
  - `셀여백제로` (line 462-474)      — primitives.set_cell_margin_zero 와 동일.
  - `표탈출` (line 913-918)          — primitives.escape_table 와 동일.

구조 (D-결정 §0 in spec/table-renderer-plan.md):
  - 1열(라벨) 폭: 25mm 고정 (D1)
  - 나머지 (N-1) 열: (usable_width - 25) ÷ (N-1) 균등 (D2)
  - 행 높이: row_height_mm (=8.4) — 한/글 자동 확장으로 본문 길이 가변 흡수
  - 셀 padding: 0mm (D-padding, tool2 권위)
  - 헤더 셀: 라벤더 배경 + HY헤드라인M 12pt + 가운데 정렬 (D7)
  - 데이터 셀: 흰색 + 휴먼명조 12pt + aligns 적용 (default left, D3)
  - 표 탈출: escape_table() (D-탈출, tool2 권위)

★ 표 위 빈 줄은 hwpx_writer._dispatch_nodes 가 자동 prepend — 본 렌더러
   내부에서 위 빈 줄 emit 금지 (다른 렌더러와 동일 규약).
"""
from __future__ import annotations

from . import primitives as p
from .base import ElementRenderer


class TableRenderer(ElementRenderer):
    """GFM 표 1개 → 한/글 표 1개."""

    def render(
        self,
        headers: list[str],
        rows: list[list[str]],
        aligns: list[str] | None = None,
    ) -> None:
        """현재 캐럿 위치에 표 1개 삽입.

        Args:
            headers: 헤더 셀 텍스트 (열 수 정의)
            rows:    데이터 행 텍스트 (각 행은 len(headers) 길이 — parser 가 보장)
            aligns:  각 열 정렬 ('left'|'center'|'right'). None 이면 모두 left.
        """
        s = self.spec
        ts = s.table
        hwp = self.hwp

        if not headers:
            return  # 빈 표는 emit 안 함

        ncols = len(headers)
        nrows = 1 + len(rows)  # 헤더 1 + 데이터 N
        aligns = aligns or ["left"] * ncols
        if len(aligns) < ncols:
            aligns = aligns + ["left"] * (ncols - len(aligns))

        # ── 열 폭 산정 (D1+D2) ──────────────────────────
        # 의도하는 시각 표 폭 = (A4 210mm) - (좌+우 여백) - width_safety_mm.
        # 좌우 여백은 라이브 측정 (measure_para_margin_mm). 실패 시 spec 값.
        # 1열 = label_col_mm 고정 시각, 나머지 = 균등 분배 시각.
        live_margin_mm = p.measure_para_margin_mm(hwp)
        spec_margin_mm = float(s.margins.left + s.margins.right)
        margin_mm = live_margin_mm if live_margin_mm > 0 else spec_margin_mm
        usable_width = 210 - margin_mm - ts.width_safety_mm
        if ncols == 1:
            visual_cols_mm = [float(usable_width)]
        else:
            rest = (usable_width - ts.label_col_mm) / (ncols - 1)
            visual_cols_mm = [ts.label_col_mm] + [rest] * (ncols - 1)
        # 한/글이 ColWidth 와 별개로 셀당 default cell padding (≈ 3.67mm) 을
        # 시각 폭에 자동 추가하므로, make_table 호출 시 각 셀에서 본 값을
        # 미리 빼서 시각 폭이 의도 (visual_cols_mm) 와 일치하게 한다.
        # 진단(scripts/diagnose_table_width.py): 보정 안 하면 165mm 의도 표가
        # 시각 180mm 로 그려져 페이지 본문 폭 170mm 를 약 10mm 초과.
        cols_mm = [w - ts.cell_inflation_mm for w in visual_cols_mm]

        rows_mm = [ts.row_height_mm] * nrows

        # ── 표 생성 + 외곽 spec ─────────────────────────
        p.make_table(hwp, cols_mm, rows_mm)

        # ★ 셀 블록 선택 후 외곽/내부선/셀여백 일괄 적용.
        # 배경: `CellBorderFill` / `TablePropertyDialog` 액션은 현재 셀 또는
        # 선택 블록에만 적용. 블록 선택 없이 호출하면 첫 셀만 외곽선이 변경
        # 되어, 좌상단 셀만 한/글 기본 외곽선이 남는 사고 (검증 2026-05-18).
        # 또한 `select_all_cells` + `Cancel` 후 캐럿은 표 마지막 셀에 머물러
        # 그대로 진행하면 헤더 입력이 마지막 셀부터 시작하고 매번 새 행이
        # 자동 추가되는 2차 사고 발생 (검증 2026-05-18). 따라서 표 생성 직후
        # 첫 셀 위치를 PosBySet 으로 저장 → 셀 블록 외곽선 처리 → 첫 셀 복원.
        saved_pos = p.get_current_pos(hwp)
        p.select_all_cells(hwp)
        p.set_table_outside_margin_zero(hwp)  # 표 폭이 페이지 본문 폭 초과 방지
        p.set_cell_margin_zero(hwp)
        p.set_table_border_color(hwp, *ts.border_color)
        p.set_table_border_thickness(
            hwp, ts.border_thick, ts.border_thick,
            ts.border_thick, ts.border_thick,
        )
        # 내부선도 외곽과 동일 색·굵기 (subsection 그리드 일관 시각)
        p.set_table_inner_line_color(hwp, *ts.border_color)
        p.set_table_inner_line_thickness(hwp, ts.border_thick, ts.border_thick)
        p.run(hwp, "Cancel")              # 블록 해제 (캐럿은 마지막 셀에 남음)
        p.set_current_pos(hwp, saved_pos) # 표 생성 직후 위치 = 첫 셀로 복원

        # ── 헤더 행 (라벤더 배경 + 가운데) ───────────────
        last_idx = ncols * nrows - 1
        cell_idx = 0
        for h in headers:
            p.set_table_bg(hwp, *ts.header_bg)
            p.set_font(hwp, ts.header_font, ts.header_size_pt, bold=False)
            p.align(hwp, "center")
            p.insert_text(hwp, h)
            if cell_idx < last_idx:
                p.move_table_right(hwp)
            cell_idx += 1

        # ── 데이터 행 (배경 미설정 = 흰색 + aligns 적용) ──
        for row in rows:
            for col, cell in enumerate(row):
                p.set_font(hwp, ts.body_font, ts.body_size_pt, bold=False)
                p.align(hwp, aligns[col])
                p.insert_text(hwp, cell)
                if cell_idx < last_idx:
                    p.move_table_right(hwp)
                cell_idx += 1

        # ── 표 탈출 (tool2 권위) ────────────────────────
        p.escape_table(hwp)
        p.align(hwp, "justify")
