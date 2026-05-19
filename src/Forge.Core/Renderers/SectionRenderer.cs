// 중제목 (Ⅰ./Ⅱ./...).
// tool2 매핑: `금감원페이지중제목(숫자, 내용)` (한컴라이브러리.py:14260-14284)

using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Renderers;

public sealed class SectionRenderer : ElementRenderer
{
    public SectionRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    private static readonly Dictionary<int, string> RomanMap = new()
    {
        [1] = "Ⅰ", [2] = "Ⅱ", [3] = "Ⅲ", [4] = "Ⅳ", [5] = "Ⅴ",
        [6] = "Ⅵ", [7] = "Ⅶ", [8] = "Ⅷ", [9] = "Ⅸ", [10] = "Ⅹ",
        [11] = "Ⅺ", [12] = "Ⅻ",
    };

    public static string ToRoman(int n) => RomanMap.TryGetValue(n, out var r) ? r : n.ToString();

    /// <summary>파란 밑줄 표 안에 'Ⅰ. 본문' 1줄 렌더링.</summary>
    public void Render(int number, string title)
    {
        var s = Spec;

        // 줄 시작 아니면 break (cursor 모드 안전망). 위 빈 줄 prepend 는 dispatcher 책임.
        if (!IsAtLineStart(Hwp)) BreakPara(Hwp);
        AlignPara(Hwp, Align.Justify);

        // 1×1 표
        double usableWidth = 205 - (s.Margins.Left + s.Margins.Right);
        MakeTable(Hwp, new[] { usableWidth }, new[] { s.SectionBoxHeightMm });
        SetCellMarginZero(Hwp);

        // 외곽 테두리: 하단만 실선
        SetTableBorderType(Hwp, BorderType.None, BorderType.Solid, BorderType.None, BorderType.None);
        SetTableBorderThickness(Hwp, 6, 8, 6, 6);
        SetTableBorderColor(Hwp, s.SectionUnderlineRgb);

        // 숫자 부분
        SetFont(Hwp, s.SectionNumberFont, s.SectionNumberSizePt, bold: s.SectionNumberBold);
        InsertText(Hwp, $"{ToRoman(number)}. ");

        // 내용 부분
        CharNormal(Hwp);
        SetFont(Hwp, s.SectionTitleFont, s.SectionTitleSizePt, bold: false);
        InsertText(Hwp, title);

        // 표 탈출
        ExitTableAndJustify(Hwp);
    }
}
