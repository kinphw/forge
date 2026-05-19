// 소제목 (가./나./[1]/[2]).
// tool2 매핑: `금감원페이지소제목(번호, 내용)` (한컴라이브러리.py:14287-14316)
// 3 셀 표 (마커 셀 + 1mm 분리 셀 + 내용 셀).

using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Renderers;

public sealed class SubsectionRenderer : ElementRenderer
{
    public SubsectionRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    /// <summary>라벤더 박스 마커 + 진파랑 테두리 내용 셀로 소제목 렌더링.</summary>
    /// <param name="marker">'가' / '나' / '1' / '2' 등 (1자~몇 자 짧은 라벨).</param>
    /// <param name="title">'개요' / '진행상황' 등 본문.</param>
    public void Render(string marker, string title)
    {
        var s = Spec;

        if (!IsAtLineStart(Hwp)) BreakPara(Hwp);
        AlignPara(Hwp, Align.Justify);

        // 3 셀 표: [마커폭, 1mm 분리, 내용폭]
        MakeTable(Hwp,
            new[] { s.SubsectionMarkerWidthMm, 1.0, s.SubsectionContentWidthMm },
            new[] { s.SubsectionBoxHeightMm });

        // 첫 셀 = 마커 셀
        SetTableBorderColor(Hwp, s.SubsectionBorderRgb);
        SetTableBg(Hwp, s.SubsectionMarkerBgRgb);
        SetTableBorderThickness(Hwp, 6, 6, 6, 6);

        SetFont(Hwp, s.SubsectionFont, s.SubsectionMarkerSizePt, bold: false);
        AlignPara(Hwp, Align.Center);
        InsertText(Hwp, marker);

        // → 가운데 1mm 셀 (분리)
        MoveTableRight(Hwp, 1);
        SetTableBorderType(Hwp, BorderType.None, BorderType.None, BorderType.Solid, BorderType.Solid);

        // → 내용 셀
        MoveTableRight(Hwp, 1);
        SetTableBorderColor(Hwp, s.SubsectionBorderRgb);
        SetTableBorderThickness(Hwp, 6, 6, 6, 6);
        SetFont(Hwp, s.SubsectionFont, s.SubsectionContentSizePt, bold: false);
        InsertText(Hwp, title);

        ExitTableAndJustify(Hwp);
    }
}
