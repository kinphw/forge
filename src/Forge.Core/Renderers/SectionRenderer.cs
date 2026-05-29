// 중제목 (Ⅰ./Ⅱ./...).
// 마크다운 변환의 기본 중제목 = 그라데이션 ribbon 스타일 (양식삽입 #4 중제목_그라인드 등가).
// 폰트는 spec (SectionNumberFont/SectionTitleFont — RealtimeTab 오버라이드 SSOT) 존중,
// 레이아웃만 그라데이션. 파란밑줄 스타일(구 #3 금감원페이지중제목)은 양식삽입 버튼으로 잔존.

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

    /// <summary>
    /// 제목 행 + 그라데이션 ribbon (1×2 표) 으로 'Ⅰ. 본문' 렌더링.
    /// 양식삽입 #4 중제목_그라인드 와 동일 레이아웃 — 폰트만 spec 주입.
    /// </summary>
    public void Render(int number, string title)
    {
        var s = Spec;

        // 줄 시작 아니면 break (cursor 모드 안전망). 위 빈 줄 prepend 는 dispatcher 책임.
        if (!IsAtLineStart(Hwp)) BreakPara(Hwp);
        AlignPara(Hwp, Align.Justify);

        // 1×2 표 — 제목 행 + 그라데이션 ribbon 행 (1.3mm)
        double usableWidth = 205 - (s.Margins.Left + s.Margins.Right);
        MakeTable(Hwp, new[] { usableWidth }, new[] { s.SectionBoxHeightMm, 1.3 });

        // 전체 셀 외곽/내부 라인 제거
        SelectAllCells(Hwp);
        SetTableBorderType(Hwp, BorderType.None, BorderType.None, BorderType.None, BorderType.None);
        SetTableInnerLineType(Hwp, BorderType.None, BorderType.None);
        Run(Hwp, "Cancel");
        // Cancel 후 캐럿 불확정 → (0,0) 강제 복귀 (없으면 제목이 ribbon 셀로 떨어짐)
        Hwp.MovePos(106, 0, 0);
        Hwp.MovePos(104, 0, 0);

        // ── Row 1: 제목 ──
        SetFont(Hwp, s.SectionNumberFont, s.SectionNumberSizePt, bold: s.SectionNumberBold);
        InsertText(Hwp, $"{ToRoman(number)}. ");
        CharNormal(Hwp);
        SetFont(Hwp, s.SectionTitleFont, s.SectionTitleSizePt, bold: false);
        InsertText(Hwp, title);

        // ── Row 2: 그라데이션 ribbon (#3333A0 → #E3E2F2) ──
        Run(Hwp, "TableLowerCell");
        SetFontSize(Hwp, 1.0);
        SetCellHeight(Hwp, 1.35);
        SetTableBgGradient(Hwp, 0x33, 0x33, 0xA0, 0xE3, 0xE2, 0xF2, 90);

        // 표 탈출
        ExitTableAndJustify(Hwp);
    }
}
