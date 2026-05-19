// [붙임], [붙임 N] — 새 페이지 + 라벨 박스 + 본문 셀.
//
// 사용자 결정 (2026-04-27): NoteCallout 의 이전 (개선 전) 형식 그대로.
// 차이점: 라벨 텍스트 "붙임"/"붙임 N", 시작 시 페이지 break, 헤더 폭 확장,
//        헤더 색 진파랑(0,0,255), 본문 폰트 13pt.
//
// ★ 액션 권위: hwp-api-mcp id=15 'BreakPage' (쪽 나누기). tool2 디컴파일도
//   16개 모두 'BreakPage' — 'PageBreak' 는 HWP API 미존재로 silent fail.

using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Renderers;

public sealed class AttachmentRenderer : ElementRenderer
{
    public AttachmentRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    /// <param name="number">[붙임 1] → 1, [붙임] → null</param>
    /// <param name="lines">본문 줄들 (첫 줄은 보통 제목, 나머지는 본문).</param>
    public void Render(int? number, IReadOnlyList<string> lines)
    {
        var s = Spec;

        // 페이지 break — [붙임]은 항상 새 페이지에서 시작.
        Run(Hwp, "BreakPage");

        var label = number is not null ? $"붙임 {number}" : "붙임";

        if (!IsAtLineStart(Hwp)) BreakPara(Hwp);
        AlignPara(Hwp, Align.Justify);

        // 3 셀 표 [라벨폭, 1mm 분리, 본문폭]
        double usableWidth = 182 - (s.Margins.Left + s.Margins.Right);
        MakeTable(Hwp,
            new[] { s.AttachHeaderWidthMm, 1.0, usableWidth },
            new[] { s.NoteBoxHeightMm });

        // 라벨 셀: 진파랑 배경 + 흰 글씨 + Bold + 가운데
        SetCellMarginZero(Hwp);
        SetTableBg(Hwp, s.AttachHeaderBgRgb);
        CharBoldOn(Hwp);
        SetFont(Hwp, s.NoteHeaderFont, s.AttachHeaderSizePt, bold: true);
        SetTextColor(Hwp, s.NoteHeaderTextRgb);
        AlignPara(Hwp, Align.Center);
        InsertText(Hwp, label);

        // → 가운데 1mm 분리 셀
        MoveTableRight(Hwp, 1);
        SetTableBorderType(Hwp, BorderType.None, BorderType.None, BorderType.Solid, BorderType.Solid);

        // → 본문 셀
        MoveTableRight(Hwp, 1);
        CharNormal(Hwp);
        SetTextColor(Hwp, 0, 0, 0);
        SetFont(Hwp, s.NoteHeaderFont, s.AttachHeaderSizePt, bold: false);
        AlignPara(Hwp, Align.Justify);
        InsertFixedSpace(Hwp, 2);

        if (lines.Count > 0)
        {
            InsertText(Hwp, lines[0]);
            for (int i = 1; i < lines.Count; i++)
            {
                BreakPara(Hwp);
                InsertFixedSpace(Hwp, 2);
                InsertText(Hwp, lines[i]);
            }
        }

        ExitTableAndJustify(Hwp);
    }
}
