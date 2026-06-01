// 중제목 (Ⅰ./Ⅱ./...).
// ★ SSOT — 레이아웃 코드는 ForgeTemplates.중제목_그라인드. 이 클래스는 thin wrapper.
//   양식삽입 #11 (중제목_그라인드) 과 동일 함수 호출 (단, spec 주입).

using Forge.Core.Templates;

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

    public void Render(int number, string title)
    {
        // skipLeadingBreak: dispatcher 가 이미 EmitBlankPara 로 spacer 줄 emit — 중복 방지.
        ForgeTemplates.중제목_그라인드(Hwp, $"{ToRoman(number)}. ", title, Spec, skipLeadingBreak: true);
    }
}
