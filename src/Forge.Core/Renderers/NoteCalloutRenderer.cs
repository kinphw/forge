// [참고] 점선 박스. tool2 `금감업무정보점선박스` (한컴라이브러리.py:15755-15776)
// 자료 탭의 "업무정보 옆 ※(참고)" 버튼 형식.
//
// 구조: 1×1 점선 표, 가로 = 198 - 좌우여백, 세로 12mm.
//   본문: "※ " (plain) + "(참고)" (Bold) + " <text>" (plain). 우정렬.

using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Renderers;

public sealed class NoteCalloutRenderer : ElementRenderer
{
    public NoteCalloutRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    /// <param name="lines">[참고] 다음 본문 줄들. 첫 줄이 박스 안 한 줄에 들어가고
    /// 추가 줄은 BreakPara 후 같은 셀 누적.</param>
    public void Render(IReadOnlyList<string> lines)
    {
        var s = Spec;

        if (!IsAtLineStart(Hwp)) BreakPara(Hwp);
        AlignPara(Hwp, Align.Right);  // tool2: ParagraphShapeAlignRight

        // 1×1 점선 표
        double usableWidth = 198 - (s.Margins.Left + s.Margins.Right);
        MakeTable(Hwp, new[] { usableWidth }, new[] { 12.0 });

        // 점선 테두리 + 굵기 2
        SetTableBorderType(Hwp, BorderType.Dotted, BorderType.Dotted, BorderType.Dotted, BorderType.Dotted);
        SetTableBorderThickness(Hwp, 2, 2, 2, 2);

        // 본문 — 맑은 고딕 13pt 검정
        SetFont(Hwp, "맑은 고딕", 13.0, bold: false);
        SetTextColor(Hwp, 0, 0, 0);
        AlignPara(Hwp, Align.Justify);

        // "※ " (plain) + "(참고)" (Bold) + " <text>" (plain)
        InsertText(Hwp, "※ ");
        CharBoldOn(Hwp);
        InsertText(Hwp, "(참고)");
        CharBoldOn(Hwp);  // toggle off

        if (lines.Count > 0)
        {
            InsertText(Hwp, " " + lines[0]);
            for (int i = 1; i < lines.Count; i++)
            {
                BreakPara(Hwp);
                InsertText(Hwp, lines[i]);
            }
        }

        ExitTableAndJustify(Hwp);
    }
}
