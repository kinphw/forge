// 보고서명 노란 박스 + 부서·일자 stamp.
//
// tool2 매핑:
//   - 노란 박스 = `금감원페이지대제목` (한컴라이브러리.py:14245-14257)
//   - stamp     = `금감원페이지` 본문 14454-14460 (인라인)
//
// spec v1.4: 보고서명만 markdown front-matter, 작성부서·작성일은 UI 에서 별도 입력.

using System.Globalization;
using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Renderers;

public sealed class MetadataRenderer : ElementRenderer
{
    public MetadataRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    /// <summary>현재 커서 위치에 헤더 1세트 (대제목 + stamp) 삽입.</summary>
    public void Render(string? reportTitle, string? department = null, string? date = null)
    {
        if (!string.IsNullOrEmpty(reportTitle))
            RenderTitleBox(reportTitle);
        if (!string.IsNullOrEmpty(department) || !string.IsNullOrEmpty(date))
            RenderStamp(department, date);
    }

    private void RenderTitleBox(string title)
    {
        var s = Spec;

        // 1×1 표 (가로: 본문 폭, 세로: 10.5mm)
        double usableWidth = 205 - (s.Margins.Left + s.Margins.Right);
        MakeTable(Hwp, new double[] { usableWidth }, new double[] { s.TitleBoxHeightMm });

        // 외곽 테두리 굵기
        var thick = s.TitleBorderThickness;
        SetTableBorderThickness(Hwp, thick, thick, thick, thick);

        // 노란 배경
        SetTableBg(Hwp, s.TitleBgRgb);

        // 글자 모양
        SetFont(Hwp, s.TitleFont, s.TitleSizePt, bold: false);
        AlignPara(Hwp, Align.Center);

        // 본문
        InsertText(Hwp, title);

        // 표 탈출
        ExitTableAndJustify(Hwp);
    }

    private void RenderStamp(string? department, string? date)
    {
        var s = Spec;

        var dept = (department ?? "").Trim();
        var dateStr = FormatDateStamp(date);
        string stamp;
        if (!string.IsNullOrEmpty(dept) && !string.IsNullOrEmpty(dateStr))
            stamp = $"({dept}, {dateStr})";
        else if (!string.IsNullOrEmpty(dept))
            stamp = $"({dept})";
        else if (!string.IsNullOrEmpty(dateStr))
            stamp = $"({dateStr})";
        else
            return;

        SetLineSpacing(Hwp, s.LineSpacingDefault);
        AlignPara(Hwp, Align.Right);
        SetFont(Hwp, s.DateFont, s.DateSizePt, bold: false);
        InsertText(Hwp, stamp);
        BreakPara(Hwp);
        AlignPara(Hwp, Align.Justify);
    }

    /// <summary>YYYY-MM-DD → '<YY>.<M>.<D>. 형식 (작은따옴표 포함).</summary>
    private static string FormatDateStamp(string? date)
    {
        if (string.IsNullOrEmpty(date)) return "";
        if (DateTime.TryParseExact(date, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return $"’{d.Year % 100}.{d.Month}.{d.Day}.";  // U+2019 = '
        }
        return date;  // 알 수 없는 형식이면 그대로
    }
}
