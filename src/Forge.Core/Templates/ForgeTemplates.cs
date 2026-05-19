// Forge 가 선별·정의한 보고서 양식 카탈로그 (11종).
// tool2 (금감원 오피스 프로그램) 의 권위 spec 에서 forge 가 직접 선별 — fss/tool2
// 의 양식 그대로가 아니라 forge 의 본 도구의 자체 양식 집합.
// Python 원본 forge/templates/fss_tool2.py 의 `금감_TEMPLATES` 1:1 포팅.
//
// 카테고리 (forge 가 그룹화):
//   - 헤더 (3):     메타헤더 / 중제목 / 소제목
//   - 참고박스 (4): 꺽쇠박스 / 점선박스 / 참고박스_마크다운 / 블루진박스
//   - 붙임박스 (1): 참고 (진파헤더)
//   - 기호 (4):     당구장 ※ / 십자가 † / 꺽쇠 「」 / 꺽쇠 『』
//
// 활성 한/글 문서의 현재 커서 위치에 emit. placeholder 글자 (◆◆◆◆◆, ◎◎◎◎◎ 등)
// 는 사용자가 한/글에서 직접 교체.

using Forge.Core.Renderers;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Templates;

public static class ForgeTemplates
{
    // ────────────────────────────────────────────────────────────────────
    // 1. 메타헤더 — md 변환의 MetadataRenderer 재호출
    // ────────────────────────────────────────────────────────────────────
    public static void 메타헤더(dynamic hwp)
    {
        new MetadataRenderer(hwp, ReportSpec.Report1).Render(
            reportTitle: "◆◆◆◆◆ 진행상황 및 대응방안",
            department: "◎◎◎◎◎◎국 ◇◇◇◇팀",
            date: DateTime.Now.ToString("yyyy-MM-dd"));
    }

    // ────────────────────────────────────────────────────────────────────
    // 2~3. 중제목 / 소제목 (Section/Subsection 직접 emit, MetadataRenderer 와 다른 spec)
    // ────────────────────────────────────────────────────────────────────
    public static void 금감원페이지중제목(dynamic hwp, string 숫자 = "Ⅰ. ", string 내용 = "◆◆◆◆◆ 진행상황")
    {
        // 1×1 표 — 하단 파란 underline + HY견명조 15pt Bold 숫자 + HY헤드라인M 16pt 본문
        EnsureNewParagraph(hwp);
        SetFontSize(hwp, 8);
        BreakPara(hwp);
        AlignPara(hwp, Align.Justify);
        // MeasurePageMarginMm 반환이 dynamic 이라 식 전체가 dynamic[] — 명시 double cast
        double w중제목 = 205.0 - (double)MeasurePageMarginMm(hwp);
        MakeTable(hwp, new double[] { w중제목 }, new double[] { 8.4 });
        SetCellMarginZero(hwp);
        SetTableBorderType(hwp, BorderType.None, BorderType.Solid, BorderType.None, BorderType.None);
        SetTableBorderThickness(hwp, 6, 8, 6, 6);
        SetTableBorderColor(hwp, 0, 0, 255);
        SetFont(hwp, "HY견명조", 15);
        hwp.HAction.Run("CharShapeBold");
        InsertText(hwp, 숫자);
        hwp.HAction.Run("CharShapeNormal");
        SetFont(hwp, "HY헤드라인M", 16);
        InsertText(hwp, 내용);
        ExitTableAndJustify(hwp);
    }

    public static void 금감원페이지소제목(dynamic hwp, string 번호 = "가", string 내용 = "개요")
    {
        // 3셀 표 (마커 / 1mm 분리 / 본문) — 라벤더 박스
        EnsureNewParagraph(hwp);
        SetFontSize(hwp, 8);
        BreakPara(hwp);
        AlignPara(hwp, Align.Justify);
        MakeTable(hwp, new[] { 7.5, 1.0, 49.0 }, new[] { 8.7 });
        SetTableBorderColor(hwp, 62, 87, 165);
        SetTableBg(hwp, 224, 229, 250);
        SetTableBorderThickness(hwp, 6, 6, 6, 6);
        SetFont(hwp, "HY헤드라인M", 15);
        AlignPara(hwp, Align.Center);
        InsertText(hwp, 번호);
        hwp.HAction.Run("TableRightCellAppend");
        SetTableBorderType(hwp, BorderType.None, BorderType.None, BorderType.Solid, BorderType.Solid);
        hwp.HAction.Run("TableRightCellAppend");
        SetTableBorderColor(hwp, 62, 87, 165);
        SetTableBorderThickness(hwp, 6, 6, 6, 6);
        SetFont(hwp, "HY헤드라인M", 15.5);
        InsertText(hwp, 내용);
        ExitTableAndJustify(hwp);
    }

    // ────────────────────────────────────────────────────────────────────
    // 4. 꺽쇠박스 — 3셀 헤더 + 본문 (복잡)
    // ────────────────────────────────────────────────────────────────────
    public static void 금감원페이지꺽쇠박스(dynamic hwp)
    {
        EnsureNewParagraph(hwp);
        SetFontSize(hwp, 8);
        BreakPara(hwp);
        AlignPara(hwp, Align.Right);
        MakeTable(hwp, new[] { 35.0, 83.0, 35.0 }, new[] { 2.0, 2.0, 22.0 });
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        hwp.HAction.Run("TableCellBlockExtend");
        SetTableBorderType(hwp, BorderType.None, BorderType.None, BorderType.None, BorderType.None);
        SetTableInnerLineType(hwp, BorderType.None, BorderType.None);
        SetFontSize(hwp, 3);
        hwp.HAction.Run("Cancel");
        hwp.MovePos(106, 0, 0);
        hwp.MovePos(104, 0, 0);
        MoveTableRight(hwp, 1);
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        hwp.MovePos(103, 0, 0);
        hwp.HAction.Run("TableMergeCell");
        AlignPara(hwp, Align.Center);
        SetFont(hwp, "맑은 고딕", 13);
        hwp.HAction.Run("CharShapeBold");
        InsertText(hwp, "〈");
        hwp.HAction.Run("InsertFixedWidthSpace");
        InsertText(hwp, "◈◈◈◈ 관련 현황");
        hwp.HAction.Run("InsertFixedWidthSpace");
        InsertText(hwp, "〉");
        MoveTableRight(hwp, 2);
        SetTableBorderSingleLine(hwp, BorderSide.Top, 1, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Left, 1, 1);
        MoveTableRight(hwp, 2);
        SetTableBorderSingleLine(hwp, BorderSide.Top, 1, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Right, 1, 1);
        MoveTableRight(hwp, 1);
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        MoveTableRight(hwp, 2);
        hwp.HAction.Run("TableMergeCell");
        SetTableBorderSingleLine(hwp, BorderSide.Left, 1, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Right, 1, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Bottom, 1, 1);
        SetFont(hwp, "맑은 고딕", 13);
        hwp.HAction.Run("InsertFixedWidthSpace");
        InsertText(hwp, "※ 맑은고딕 13pt");
        BreakPara(hwp);
        SetFontSize(hwp, 4);
        BreakPara(hwp);
        SetFontSize(hwp, 13);
        hwp.HAction.Run("InsertFixedWidthSpace");
        hwp.HAction.Run("InsertFixedWidthSpace");
        InsertText(hwp, "◦ 맑은고딕 13pt");
        BreakPara(hwp);
        SetFontSize(hwp, 3);
        BreakPara(hwp);
        SetFontSize(hwp, 11);
        for (int i = 0; i < 7; i++) hwp.HAction.Run("InsertFixedWidthSpace");
        InsertText(hwp, "* 맑은고딕 11pt");
        ExitTableAndJustify(hwp);
    }

    // ────────────────────────────────────────────────────────────────────
    // 5. 점선박스 — 결론 ⇨ 민트 배경
    // ────────────────────────────────────────────────────────────────────
    public static void 금감원페이지점선박스(dynamic hwp)
    {
        EnsureNewParagraph(hwp);
        SetFontSize(hwp, 8);
        BreakPara(hwp);
        AlignPara(hwp, Align.Right);
        double w점선 = 199.5 - (double)MeasurePageMarginMm(hwp);
        MakeTable(hwp, new double[] { w점선 }, new double[] { 18.0 });
        SetTableBorderType(hwp, BorderType.Dotted, BorderType.Dotted, BorderType.Dotted, BorderType.Dotted);
        SetTableBorderThickness(hwp, 2, 2, 2, 2);
        SetTableBg(hwp, 205, 242, 228);
        SetFont(hwp, "휴먼명조", 15);
        InsertText(hwp, "⇨ 휴먼명조 15pt");
        ExitTableAndJustify(hwp);
    }

    // ────────────────────────────────────────────────────────────────────
    // 6. 참고박스 (마크다운) — NoteCalloutRenderer 재호출
    // ────────────────────────────────────────────────────────────────────
    public static void 참고박스_마크다운(dynamic hwp)
    {
        new NoteCalloutRenderer(hwp, ReportSpec.Report1).Render(new[] { "ㅇㅇㅇ", "ㅁㅁㅁ" });
    }

    // ────────────────────────────────────────────────────────────────────
    // 7. 블루진박스 — 진남 헤더 + 본문 (꺽쇠박스와 비슷한 패턴)
    // ────────────────────────────────────────────────────────────────────
    public static void 금감보고서블루진박스(dynamic hwp)
    {
        EnsureNewParagraph(hwp);
        SetFontSize(hwp, 10);
        BreakPara(hwp);
        AlignPara(hwp, Align.Right);
        MakeTable(hwp, new[] { 47.0, 59.0, 47.0 }, new[] { 2.7, 2.7, 36.0 });
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        hwp.HAction.Run("TableCellBlockExtend");
        SetTableBorderType(hwp, BorderType.None, BorderType.None, BorderType.None, BorderType.None);
        SetTableInnerLineType(hwp, BorderType.None, BorderType.None);
        SetFontSize(hwp, 3);
        hwp.HAction.Run("Cancel");
        hwp.MovePos(106, 0, 0);
        hwp.MovePos(104, 0, 0);
        MoveTableRight(hwp, 1);
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        hwp.MovePos(103, 0, 0);
        hwp.HAction.Run("TableMergeCell");
        SetTableBg(hwp, 58, 60, 132);
        SetTextColor(hwp, 255, 255, 255);
        AlignPara(hwp, Align.Center);
        SetFont(hwp, "맑은 고딕", 12);
        hwp.HAction.Run("CharShapeBold");
        InsertText(hwp, "맑은고딕 12pt");
        MoveTableRight(hwp, 2);
        SetTableBorderSingleLine(hwp, BorderSide.Top, 1, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Left, 1, 1);
        MoveTableRight(hwp, 2);
        SetTableBorderSingleLine(hwp, BorderSide.Top, 1, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Right, 1, 1);
        MoveTableRight(hwp, 1);
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        MoveTableRight(hwp, 2);
        hwp.HAction.Run("TableMergeCell");
        SetTableBorderSingleLine(hwp, BorderSide.Left, 1, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Right, 1, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Bottom, 1, 1);
        SetFont(hwp, "맑은 고딕", 13);
        InsertText(hwp, "▣ 맑은고딕 13pt");
        SetCellVerticalAlign(hwp, 0);
        ExitTableAndJustify(hwp);
    }

    // ────────────────────────────────────────────────────────────────────
    // 8. 참고 (진파헤더) — 라벨 + 본문 callout
    // ────────────────────────────────────────────────────────────────────
    public static void 금감원페이지참고(dynamic hwp)
    {
        double w참고 = 182.0 - (double)MeasurePageMarginMm(hwp);
        MakeTable(hwp, new double[] { 17.6, 1.0, w참고 }, new double[] { 8.7 });
        SetCellMarginZero(hwp);
        SetTableBg(hwp, 0, 0, 255);
        hwp.HAction.Run("CharShapeBold");
        SetFont(hwp, "HY헤드라인M", 15);
        SetTextColor(hwp, 255, 255, 255);
        AlignPara(hwp, Align.Center);
        InsertText(hwp, "참고");
        MoveTableRight(hwp, 1);
        SetTableBorderType(hwp, BorderType.None, BorderType.None, BorderType.Solid, BorderType.Solid);
        hwp.HAction.Run("TableResizeExLeft");
        hwp.HAction.Run("TableResizeExLeft");
        MoveTableRight(hwp, 1);
        SetFont(hwp, "HY헤드라인M", 15);
        hwp.HAction.Run("InsertFixedWidthSpace");
        hwp.HAction.Run("InsertFixedWidthSpace");
        InsertText(hwp, "HY헤드라인M 15pt");
        ExitTableAndJustify(hwp);
    }

    // ────────────────────────────────────────────────────────────────────
    // 9~12. 기호 — 단순 InsertText 1~2 글자
    // ────────────────────────────────────────────────────────────────────
    public static void 당구장(dynamic hwp) => InsertText(hwp, "※");

    public static void 십자가(dynamic hwp) => InsertText(hwp, "†");

    public static void 꺽쇠_홑(dynamic hwp)
    {
        // 맑은 고딕으로 박고 typing attr 복귀. 캐럿은 」 뒤.
        using (TempFontFace(hwp, "맑은 고딕"))
        {
            InsertText(hwp, "「」");
        }
    }

    public static void 꺽쇠_겹(dynamic hwp)
    {
        using (TempFontFace(hwp, "맑은 고딕"))
        {
            InsertText(hwp, "『』");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // 내부 helper
    // ────────────────────────────────────────────────────────────────────

    /// <summary>현재 위치가 단락 중간이면 BreakPara — 양식이 새 단락에서 시작 보장.</summary>
    private static void EnsureNewParagraph(dynamic hwp)
    {
        try
        {
            var pos = hwp.GetPosBySet();
            if ((int)pos.Item("Pos") != 0)
                hwp.HAction.Run("BreakPara");
        }
        catch { /* 위치 가져오기 실패 — 무시 */ }
    }

    // ────────────────────────────────────────────────────────────────────
    // 카탈로그 — TemplatesTab 이 사용
    // ────────────────────────────────────────────────────────────────────

    public sealed record TemplateEntry(
        int Num,
        string Group,
        Action<object> Invoke,   // dynamic 호환 위해 object → callsite cast
        string Label,
        string Description);

    public static readonly IReadOnlyList<TemplateEntry> All = new TemplateEntry[]
    {
        // ── 헤더 ──
        new(1, "헤더", h => 메타헤더((dynamic)h),
            "메타헤더 (제목+팀+일자)", "노란박스 + 부서·일자 stamp (샘플값)"),
        new(2, "헤더", h => 금감원페이지중제목((dynamic)h),
            "중제목 (Ⅰ./Ⅱ.)", "파란밑줄 HY견명조+HY헤드라인M"),
        new(3, "헤더", h => 금감원페이지소제목((dynamic)h),
            "소제목 (가./나.)", "라벤더 마커+본문 2셀"),
        // ── 참고박스 ──
        new(4, "참고박스", h => 금감원페이지꺽쇠박스((dynamic)h),
            "꺽쇠박스", "〈◈◈◈ 관련 현황〉 3셀 헤더"),
        new(5, "참고박스", h => 금감원페이지점선박스((dynamic)h),
            "점선박스", "⇨ 결론 점선박스 민트"),
        new(6, "참고박스", h => 참고박스_마크다운((dynamic)h),
            "참고박스 (마크다운 변환)", "md [참고] 점선박스 + ※(참고)"),
        new(7, "참고박스", h => 금감보고서블루진박스((dynamic)h),
            "블루진박스", "진남 헤더 + 12pt Bold + 본문"),
        // ── 붙임박스 ──
        new(8, "붙임박스", h => 금감원페이지참고((dynamic)h),
            "참고 (진파헤더)", "진파 라벨 + HY헤드라인M 15pt 본문"),
        // ── 기호 ──
        new(9,  "기호", h => 당구장((dynamic)h),
            "당구장 ※", "주석 마커 ※ 1 글자"),
        new(10, "기호", h => 십자가((dynamic)h),
            "십자가 †", "보조 주석 마커 † 1 글자"),
        new(11, "기호", h => 꺽쇠_홑((dynamic)h),
            "꺽쇠 「」 (홑)", "맑은 고딕 「」, 캐럿은 」 뒤"),
        new(12, "기호", h => 꺽쇠_겹((dynamic)h),
            "꺽쇠 『』 (겹)", "맑은 고딕 『』, 책·문헌 제목"),
    };
}
