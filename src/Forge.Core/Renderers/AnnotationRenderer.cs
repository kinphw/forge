// 주석 (* ※ †). 3 종 마커 모두 Spec.Annotation 단일 spec.
// 출력 글리프는 입력 마커 그대로 보존.

using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Renderers;

public sealed class AnnotationRenderer : ElementRenderer
{
    public AnnotationRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    /// <param name="marker">'*' / '**' / '***' / '※' / '†' — 그대로 출력.</param>
    /// <param name="body">주석 본문.</param>
    public void Render(string marker, string body)
    {
        var a = Spec.Annotation;

        SetFont(Hwp, a.Font, a.SizePt, bold: a.Bold);
        SetLineSpacing(Hwp, a.LineSpacing);
        SetIndent(Hwp, a.IndentPt);
        AlignPara(Hwp, Align.Justify);

        InsertFixedSpace(Hwp, a.FixedPre);
        InsertText(Hwp, marker);
        InsertFixedSpace(Hwp, a.FixedPost);
        InsertText(Hwp, body);
        BreakPara(Hwp);
    }
}
