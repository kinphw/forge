// GFM 부분집합 표 (헤더 + 구분선 + 데이터 N행) → 한/글 표.
//
// tool2 권위:
//   - `행안부초록표` (한컴라이브러리.py:3053+) — 다행 N×M 패턴 1:1
//   - `표만들기` (line 736-751)        = Primitives.MakeTable
//   - `셀여백제로` (line 462-474)      = Primitives.SetCellMarginZero
//   - `표탈출` (line 913-918)          = Primitives.EscapeTable
//
// 구조:
//   - 1열(라벨) 폭: 25mm 고정 (D1)
//   - 나머지 (N-1) 열: (usable_width - 25) ÷ (N-1) 균등 (D2)
//   - 행 높이: row_height_mm (=8.4)
//   - 셀 padding: 0mm (D-padding, tool2 권위)
//   - 헤더: 라벤더 배경 + HY헤드라인M 12pt + 가운데
//   - 데이터: 흰색 + 휴먼명조 12pt + aligns 적용 (default left)
//
// ★ 표 위 빈 줄은 HwpxWriter dispatcher 가 자동 prepend — 본 렌더러 내부에서 emit 금지.

using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Renderers;

public sealed class TableRenderer : ElementRenderer
{
    public TableRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    /// <param name="headers">헤더 셀 텍스트 (열 수 정의).</param>
    /// <param name="rows">데이터 행 — 각 행은 len(headers) 길이 (parser 가 보장).</param>
    /// <param name="aligns">각 열 정렬 'left'|'center'|'right'. null/짧으면 left 패딩.</param>
    public void Render(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<string>? aligns = null)
    {
        var ts = Spec.Table;

        if (headers.Count == 0) return;  // 빈 표는 emit 안 함

        int ncols = headers.Count;
        int nrows = 1 + rows.Count;

        var effectiveInputAligns = aligns is null
            ? Enumerable.Repeat("left", ncols).ToList()
            : aligns.ToList();
        if (effectiveInputAligns.Count < ncols)
            effectiveInputAligns.AddRange(Enumerable.Repeat("left", ncols - effectiveInputAligns.Count));

        // ── 열 폭 산정 (D1+D2) ──────────────────────────
        double liveMarginMm = MeasurePageMarginMm(Hwp);
        double specMarginMm = Spec.Margins.Left + Spec.Margins.Right;
        double marginMm = liveMarginMm > 0 ? liveMarginMm : specMarginMm;
        double usableWidth = 210 - marginMm - ts.WidthSafetyMm;

        List<double> visualColsMm;
        if (ncols == 1)
        {
            visualColsMm = new List<double> { usableWidth };
        }
        else
        {
            double rest = (usableWidth - ts.LabelColMm) / (ncols - 1);
            visualColsMm = new List<double>(ncols) { ts.LabelColMm };
            for (int i = 1; i < ncols; i++) visualColsMm.Add(rest);
        }
        // 한/글 default cell padding (≈ 3.67mm) 시각 폭 보정 — make_table 전 미리 차감.
        var colsMm = visualColsMm.Select(w => w - ts.CellInflationMm).ToList();
        var rowsMm = Enumerable.Repeat(ts.RowHeightMm, nrows).ToList();

        // ── 표 생성 + 외곽 spec ─────────────────────────
        MakeTable(Hwp, colsMm, rowsMm);

        // 셀 블록 선택 → 외곽/내부선/셀여백 일괄 적용 → 첫 셀로 복원.
        // (블록 없이 호출 시 첫 셀만 적용 — 검증 2026-05-18 사고)
        // 또 SelectAllCells + Cancel 후 캐럿이 마지막 셀에 머물러 헤더 입력이 어긋남
        // — get/set PosBySet 으로 첫 셀 위치 보존.
        var savedPos = GetCurrentPos(Hwp);
        SelectAllCells(Hwp);
        SetTableOutsideMarginZero(Hwp);
        SetCellMarginZero(Hwp);
        SetTableBorderColor(Hwp, ts.BorderColor);
        SetTableBorderThickness(Hwp, ts.BorderThick, ts.BorderThick, ts.BorderThick, ts.BorderThick);
        SetTableInnerLineColor(Hwp, ts.BorderColor);
        SetTableInnerLineThickness(Hwp, ts.BorderThick, ts.BorderThick);
        if (ts.HideSideBorders)
            SetTableBorderType(Hwp,
                BorderType.Solid, BorderType.Solid,
                BorderType.None,  BorderType.None);
        Run(Hwp, "Cancel");
        SetCurrentPos(Hwp, savedPos);

        // ── 헤더 행 ────────────────────────────────────
        int lastIdx = ncols * nrows - 1;
        int cellIdx = 0;
        foreach (var h in headers)
        {
            SetTableBg(Hwp, ts.HeaderBg);
            SetFont(Hwp, ts.HeaderFont, ts.HeaderSizePt, bold: ts.HeaderBold);
            AlignPara(Hwp, Align.Center);
            InsertText(Hwp, h);
            if (cellIdx < lastIdx) MoveTableRight(Hwp);
            cellIdx++;
        }

        // ── 데이터 행 ──────────────────────────────────
        // GFM aligns 가 전부 default "left" 면 body_align(=center) override.
        // 어떤 셀이라도 명시 (`:---` 등) 있으면 aligns 그대로 존중.
        bool allDefault = effectiveInputAligns.All(a => a == "left");
        var effectiveAligns = allDefault
            ? Enumerable.Repeat(ts.BodyAlign, ncols).ToList()
            : effectiveInputAligns;
        foreach (var row in rows)
        {
            for (int col = 0; col < row.Count; col++)
            {
                SetFont(Hwp, ts.BodyFont, ts.BodySizePt, bold: false);
                SetLineSpacing(Hwp, ts.BodyLineSpacing);
                AlignPara(Hwp, effectiveAligns[col]);
                InsertText(Hwp, row[col]);
                if (cellIdx < lastIdx) MoveTableRight(Hwp);
                cellIdx++;
            }
        }

        // ── 표 탈출 (tool2 권위) ──────────────────────
        EscapeTable(Hwp);
        AlignPara(Hwp, Align.Justify);
    }
}
