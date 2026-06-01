// 결론 화살표 박스 (=>).
// tool2 매핑: `금감원페이지점선박스` (한컴라이브러리.py:14398-14417)
// 민트 배경 + 점선 테두리 1×1 표 안에 ⇨ + 본문.

using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Renderers;

public sealed class ConclusionRenderer : ElementRenderer
{
    public ConclusionRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    /// <param name="body">=> 다음에 오는 결론 텍스트. ⇨ 글머리는 자동 prepend.</param>
    public void Render(string body)
    {
        var s = Spec;

        if (!IsAtLineStart(Hwp)) BreakPara(Hwp);
        // ★ 2026-06-02: body 단락은 Justify. 기존엔 Right 였으나 진단 로그상
        //   Right-aligned 단락에서 CloseEx 후 MoveLineDown 이 no-op → 캐럿이 박스
        //   앞에 잔류 → 후속 BreakPara 가 박스 위 분할 → 박스가 끝으로 밀리는
        //   cascade. 본문 폭(165mm) 대비 표 폭(159.5mm) 라 시각 차이 5.5mm 미미.
        AlignPara(Hwp, Align.Justify);

        // 1×1 표 (가로 199.5 - 여백, 세로 18mm)
        double usableWidth = 199.5 - (s.Margins.Left + s.Margins.Right);
        MakeTable(Hwp, new[] { usableWidth }, new[] { s.ConclusionBoxHeightMm });

        // 외곽 점선 + 굵기 2 + 민트 배경
        var borderType = s.ConclusionBorderDotted ? BorderType.Dotted : BorderType.Solid;
        SetTableBorderType(Hwp, borderType, borderType, borderType, borderType);
        SetTableBorderThickness(Hwp, 2, 2, 2, 2);
        SetTableBg(Hwp, s.ConclusionBgRgb);

        // 본문 (휴먼명조 15pt, ⇨ + 본문)
        SetFont(Hwp, s.ConclusionFont, s.ConclusionSizePt, bold: false);
        InsertText(Hwp, $"⇨ {body}");

        ExitTableAndJustify(Hwp);
    }
}
