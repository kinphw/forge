// Forge 가 선별·정의한 보고서 양식 카탈로그 (20종).
// tool2 (금감원 오피스 프로그램) 의 권위 spec 에서 forge 가 직접 선별 — fss/tool2
// 의 양식 그대로가 아니라 forge 의 본 도구의 자체 양식 집합.
//
// 카테고리 (forge 가 그룹화):
//   - 헤더 (5):     메타헤더 / 헤더_약식 / 중제목 / 중제목_그라인드 / 소제목
//   - 목차 (1):     금감보고서목차 (Ⅰ./Ⅱ./Ⅲ. 자동 키-인)
//   - 참고박스 (6): 꺽쇠박스 / 점선박스 / 요약박스 / 화살표박스_회색 / 참고박스_마크다운 / 블루진박스
//   - 붙임박스 (1): 참고 (진파헤더)
//   - 기호 (7):     당구장 ※ / 십자가 † / 네모 □ / 동그라미 ○ / 작은동그라미 ◦ / 꺽쇠 「」 / 꺽쇠 『』
//
// 활성 한/글 문서의 현재 커서 위치에 emit. placeholder 글자 (◆◆◆◆◆, ◎◎◎◎◎ 등)
// 는 사용자가 한/글에서 직접 교체.
//
// 추가 4종 (헤더_약식·요약박스·중제목_그라인드·화살표박스_회색) 출처:
//   reference/보고서양식_base_추가4종.hwpx — 1페이지 4 양식 분석.
// 목차 출처:
//   tool2 한컴라이브러리_decompiled.py:14716-14885 (금감보고서목차 + 금감보고서목차제목).

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

    public static void 금감원페이지소제목(dynamic hwp, string 번호 = "가", string 내용 = "개요", ReportSpec? spec = null, bool skipLeadingBreak = false)
    {
        // ★ SSOT — markdown SubsectionRenderer 가 이 함수를 호출. spec null 이면 양식삽입 기본값.
        //   skipLeadingBreak=true: 호출자(마크다운 dispatcher) 가 이미 박스 앞 spacer 줄을
        //   emit 한 경우 중복 방지. EnsureNewParagraph (안전망) 은 유지.
        string font          = spec?.SubsectionFont           ?? "HY헤드라인M";
        double markerSize    = spec?.SubsectionMarkerSizePt   ?? 15.0;
        double contentSize   = spec?.SubsectionContentSizePt  ?? 15.5;
        double boxH          = spec?.SubsectionBoxHeightMm    ?? 8.7;
        double markerW       = spec?.SubsectionMarkerWidthMm  ?? 7.5;
        double contentW      = spec?.SubsectionContentWidthMm ?? 49.0;
        Rgb    markerBg      = spec?.SubsectionMarkerBgRgb    ?? new Rgb(224, 229, 250);
        Rgb    borderRgb     = spec?.SubsectionBorderRgb      ?? new Rgb(62, 87, 165);

        // 3셀 표 (마커 / 1mm 분리 / 본문) — 라벤더 박스
        EnsureNewParagraph(hwp);
        if (!skipLeadingBreak)
        {
            SetFontSize(hwp, 8);
            BreakPara(hwp);
        }
        AlignPara(hwp, Align.Justify);
        MakeTable(hwp, new[] { markerW, 1.0, contentW }, new[] { boxH });
        SetTableBorderColor(hwp, borderRgb);
        SetTableBg(hwp, markerBg);
        SetTableBorderThickness(hwp, 6, 6, 6, 6);
        SetFont(hwp, font, markerSize);
        AlignPara(hwp, Align.Center);
        InsertText(hwp, 번호);
        hwp.HAction.Run("TableRightCellAppend");
        SetTableBorderType(hwp, BorderType.None, BorderType.None, BorderType.Solid, BorderType.Solid);
        hwp.HAction.Run("TableRightCellAppend");
        SetTableBorderColor(hwp, borderRgb);
        SetTableBorderThickness(hwp, 6, 6, 6, 6);
        SetFont(hwp, font, contentSize);
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
    // 13. 헤더 약식 — 상하 진남 라인 (top/bottom 0.7mm #3E57A5), 좌우 없음
    //     reference/보고서양식_base_추가4종.hwpx borderFillIDRef="10".
    //     본문 HY헤드라인M 18pt 가운데정렬.
    // ────────────────────────────────────────────────────────────────────
    public static void 헤더_약식(dynamic hwp, string 내용 = "◆◆◆◆◆ 관련 쟁점 검토")
    {
        EnsureNewParagraph(hwp);
        SetFontSize(hwp, 8);
        BreakPara(hwp);
        AlignPara(hwp, Align.Justify);
        double w = 210.0 - (double)MeasurePageMarginMm(hwp);
        MakeTable(hwp, new[] { w }, new[] { 11.2 });
        // ★ 가운데 정렬 먼저 (borders/font 이전) — CellBorderFill 후에 ParagraphShapeAlignCenter
        //   action 이 안 먹는 환경 있음 (테스트 사고). ParaShape SetParam 으로 AlignType=3
        //   직접 + AlignPara action 한 번 더 (belt+suspenders).
        ComHelpers.SetParam(hwp, "ParagraphShape", new Dictionary<string, object> { ["AlignType"] = 3 });
        AlignPara(hwp, Align.Center);
        SetTableBorderType(hwp, BorderType.Solid, BorderType.Solid, BorderType.None, BorderType.None);
        SetTableBorderThickness(hwp, 9, 9, 6, 6);  // 9 ≈ 0.7mm
        SetTableBorderColor(hwp, 62, 87, 165);     // #3E57A5
        SetFont(hwp, "HY헤드라인M", 18);
        AlignPara(hwp, Align.Center);  // borders/font 변경이 align 을 흔들 수 있어 한 번 더
        InsertText(hwp, 내용);
        ExitTableAndJustify(hwp);
    }

    // ────────────────────────────────────────────────────────────────────
    // 14. 요약박스 — 0.2mm 검정 테두리 + 라벤더 배경 (#EBDEF1) + 한컴윤고딕 14.5pt
    //     reference borderFillIDRef="9". ◈ 마커 + 본문.
    // ────────────────────────────────────────────────────────────────────
    public static void 요약박스(dynamic hwp, string 내용 = "◆◆◆◆ 도입에 따른 ◇◇◇◇ 검토")
    {
        EnsureNewParagraph(hwp);
        SetFontSize(hwp, 8);
        BreakPara(hwp);
        AlignPara(hwp, Align.Justify);
        double w = 210.0 - (double)MeasurePageMarginMm(hwp);
        MakeTable(hwp, new[] { w }, new[] { 16.2 });
        SetTableBorderType(hwp, BorderType.Solid, BorderType.Solid, BorderType.Solid, BorderType.Solid);
        SetTableBorderThickness(hwp, 3, 3, 3, 3);   // 3 ≈ 0.2mm
        SetTableBorderColor(hwp, 0, 0, 0);
        SetTableBg(hwp, 235, 222, 241);             // #EBDEF1
        SetFont(hwp, "한컴 윤고딕 240", 14.5);
        AlignPara(hwp, Align.Left);
        InsertText(hwp, " ◈  ");
        InsertText(hwp, 내용);
        SetCellVerticalAlign(hwp, 1);               // 1=가운데
        ExitTableAndJustify(hwp);
    }

    // ────────────────────────────────────────────────────────────────────
    // 15. 중제목 (그라인드) — 2행 1열, 1행 제목 + 2행 그라데이션 ribbon
    //     reference borderFillIDRef="7" (gradient #3333A0 → #E3E2F2 linear 90°).
    //     상단 셀: HY견명조 Bold 숫자 + HY헤드라인M 본문 16pt.
    //     하단 셀: 1.3mm 얇은 그라데이션 ribbon + 0.5mm 바닥선.
    // ────────────────────────────────────────────────────────────────────
    public static void 중제목_그라인드(dynamic hwp, string 숫자 = "Ⅰ. ", string 내용 = "◆◆◆◆◆ 진행상황", ReportSpec? spec = null, bool skipLeadingBreak = false)
    {
        // ★ SSOT — markdown SectionRenderer 가 이 함수를 호출. spec null 이면 양식삽입 기본값.
        //   skipLeadingBreak=true: 호출자(마크다운 dispatcher) 가 이미 박스 앞 spacer 줄을
        //   emit 한 경우 중복 방지. EnsureNewParagraph (안전망) 은 유지.
        string numberFont = spec?.SectionNumberFont ?? "HY견명조";
        double numberSize = spec?.SectionNumberSizePt ?? 16.0;
        bool   numberBold = spec?.SectionNumberBold   ?? true;
        string titleFont  = spec?.SectionTitleFont    ?? "HY헤드라인M";
        double titleSize  = spec?.SectionTitleSizePt  ?? 16.0;
        double titleH     = spec?.SectionBoxHeightMm  ?? 9.3;

        EnsureNewParagraph(hwp);
        if (!skipLeadingBreak)
        {
            SetFontSize(hwp, 8);
            BreakPara(hwp);
        }
        AlignPara(hwp, Align.Justify);

        double w = (spec is not null)
            ? 205.0 - (spec.Margins.Left + spec.Margins.Right)
            : 205.0 - (double)MeasurePageMarginMm(hwp);
        MakeTable(hwp, new[] { w }, new[] { titleH, 1.3 });

        // 전체 셀 선택 → 외곽/내부 라인 모두 제거 (셀 사이 hairline 까지)
        SelectAllCells(hwp);
        SetTableBorderType(hwp, BorderType.None, BorderType.None, BorderType.None, BorderType.None);
        SetTableInnerLineType(hwp, BorderType.None, BorderType.None);
        hwp.HAction.Run("Cancel");
        // ★ Cancel 후 캐럿 위치 불확정 — tool2 패턴: MovePos(106=열 시작) + MovePos(104=행 시작)
        //   = 표의 (0,0) top-left 셀로 강제 복귀. 없으면 title 이 그라데이션 ribbon 셀로
        //   들어가는 사고 (제목 row 1 에 가야 하는데 row 2 에 떨어짐).
        hwp.MovePos(106, 0, 0);
        hwp.MovePos(104, 0, 0);

        // ── Row 1 (현재 caret): 제목 ──
        SetFont(hwp, numberFont, numberSize);
        if (numberBold) hwp.HAction.Run("CharShapeBold");
        InsertText(hwp, 숫자);
        hwp.HAction.Run("CharShapeNormal");
        SetFont(hwp, titleFont, titleSize);
        InsertText(hwp, 내용);

        // ── Row 2 로 이동 → 그라데이션 ribbon ──
        // ★ TableRightCellAppend (MoveTableRight) 는 행 끝에서 새 컬럼 append — 1×2 표에서
        //   (0,0) → 사고. TableLowerCell 로 명시적 아래 이동.
        hwp.HAction.Run("TableLowerCell");
        // ★ ribbon 셀은 1.35mm 얇은 띠 — hwpx 참조 charPr 17 의 height=100 (=1pt). 글자크기
        //   1pt 로 두지 않으면 default 폰트 (8pt) 가 셀 높이를 늘림 → ribbon 두께 망가짐.
        SetFontSize(hwp, 1.0);
        SetCellHeight(hwp, 1.35);    // 셀 높이 명시 고정 (글자크기만으로는 default 높이 잔존)
        // 하단 테두리 없음 — 그라데이션 fill 만 (SelectAllCells 단계에서 이미 4변 None 처리됨)
        SetTableBgGradient(hwp, 0x33, 0x33, 0xA0, 0xE3, 0xE2, 0xF2, 90);
        // ★ ribbon 셀(1.35mm/1pt) 에서 직접 ExitTableAndJustify 가 셀 탈출 못하는 케이스 회피.
        //   정상 사이즈 제목 행으로 복귀 후 exit.
        hwp.HAction.Run("TableUpperCell");
        ExitTableAndJustify(hwp);
    }

    // ────────────────────────────────────────────────────────────────────
    // 16. 화살표박스 (회색) — 얇은 검정 테두리 + 연회색 배경 (#F2F2F2) + 휴먼명조 15pt
    //     reference borderFillIDRef="8". ➡ 마커로 결론·요지 강조.
    // ────────────────────────────────────────────────────────────────────
    public static void 화살표박스_회색(dynamic hwp,
        string 내용 = "◆◆◆◆ 도입에 따른 ◇◇◇ 등 △△△을 검토")
    {
        EnsureNewParagraph(hwp);
        SetFontSize(hwp, 8);
        BreakPara(hwp);
        AlignPara(hwp, Align.Justify);
        double w = 208.0 - (double)MeasurePageMarginMm(hwp);   // 168mm @ 20mm margins
        MakeTable(hwp, new[] { w }, new[] { 17.0 });
        SetTableBorderType(hwp, BorderType.Solid, BorderType.Solid, BorderType.Solid, BorderType.Solid);
        SetTableBorderThickness(hwp, 1, 1, 1, 1);    // 1 ≈ 0.12mm
        SetTableBorderColor(hwp, 0, 0, 0);
        SetTableBg(hwp, 242, 242, 242);              // #F2F2F2
        SetFont(hwp, "휴먼명조", 15);
        AlignPara(hwp, Align.Left);
        InsertText(hwp, "➡ ");
        InsertText(hwp, 내용);
        ExitTableAndJustify(hwp);
    }

    // ────────────────────────────────────────────────────────────────────
    // 17. 금감보고서목차 — tool2 한컴라이브러리_decompiled.py:14760-14885 1:1 복제.
    //
    //     3×3 표 [43/64/43 mm × 6/6/50 mm]:
    //       · 상단 2×3 영역 병합 → nested 9-col 띠 헤더 (금감보고서목차제목)
    //       · 하단 1×3 영역 병합 → 본문 (탭점선 + Ⅰ/Ⅱ/Ⅲ 자동 키-인)
    //     모든 액션·인자·순서를 tool2 그대로 — Forge primitive 가 tool2 메서드와 1:1 매핑
    //     (표만들기→MakeTable, 표테두리타입→SetTableBorderType, 표오른쪽→MoveTableRight,
    //      표테두리단일선→SetTableBorderSingleLine, 탭점선설정→SetDottedTab, 문장→InsertText).
    // ────────────────────────────────────────────────────────────────────
    public static void 금감보고서목차(dynamic hwp)
    {
        EnsureNewParagraph(hwp);
        MakeTable(hwp, new[] { 43.0, 64.0, 43.0 }, new[] { 6.0, 6.0, 50.0 });
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        hwp.HAction.Run("TableCellBlockExtend");
        SetTableBorderType(hwp, BorderType.None, BorderType.None, BorderType.None, BorderType.None);
        SetTableInnerLineType(hwp, BorderType.None, BorderType.None);
        hwp.HAction.Run("Cancel");
        hwp.MovePos(106, 0, 0);
        hwp.MovePos(104, 0, 0);
        MoveTableRight(hwp, 1);
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        hwp.MovePos(103, 0, 0);
        hwp.HAction.Run("TableMergeCell");
        hwp.HAction.Run("ParagraphShapeAlignCenter");
        금감보고서목차제목(hwp);
        hwp.HAction.Run("Delete");
        MoveTableRight(hwp, 2);
        SetTableBorderSingleLine(hwp, BorderSide.Top, 6, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Left, 6, 1);
        MoveTableRight(hwp, 2);
        SetTableBorderSingleLine(hwp, BorderSide.Top, 6, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Right, 6, 1);
        MoveTableRight(hwp, 1);
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        MoveTableRight(hwp, 2);
        hwp.HAction.Run("TableMergeCell");
        SetTableBorderSingleLine(hwp, BorderSide.Left, 6, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Right, 6, 1);
        SetTableBorderSingleLine(hwp, BorderSide.Bottom, 6, 1);
        SetLineSpacing(hwp, 180);
        SetFontSize(hwp, 9);
        BreakPara(hwp);
        SetFontSize(hwp, 16);
        SetFontFace(hwp, "맑은 고딕");
        hwp.HAction.Run("CharShapeNormal");
        hwp.HAction.Run("CharShapeBold");
        SetDottedTab(hwp, 98400);
        InsertText(hwp, "Ⅰ. 추진 배경 ");
        hwp.HAction.Run("InsertTab");
        InsertText(hwp, " 1 ");
        BreakPara(hwp);
        SetFontSize(hwp, 4);
        BreakPara(hwp);
        SetFontSize(hwp, 16);
        InsertText(hwp, "Ⅱ. 추진 방향 ");
        hwp.HAction.Run("InsertTab");
        InsertText(hwp, " 2 ");
        BreakPara(hwp);
        hwp.HAction.Run("CharShapeNormal");
        InsertText(hwp, "  1. ");
        hwp.HAction.Run("InsertTab");
        InsertText(hwp, " 3 ");
        BreakPara(hwp);
        InsertText(hwp, "  2. ");
        hwp.HAction.Run("InsertTab");
        InsertText(hwp, " 4 ");
        BreakPara(hwp);
        InsertText(hwp, "    가. ");
        hwp.HAction.Run("InsertTab");
        InsertText(hwp, " 4 ");
        BreakPara(hwp);
        InsertText(hwp, "    나. ");
        hwp.HAction.Run("InsertTab");
        InsertText(hwp, " 5 ");
        BreakPara(hwp);
        InsertText(hwp, "    다. ");
        hwp.HAction.Run("InsertTab");
        InsertText(hwp, " 6 ");
        BreakPara(hwp);
        SetFontSize(hwp, 4);
        BreakPara(hwp);
        SetFontSize(hwp, 16);
        hwp.HAction.Run("CharShapeBold");
        InsertText(hwp, "Ⅲ. 향후 계획 ");
        hwp.HAction.Run("InsertTab");
        InsertText(hwp, " 7 ");
        BreakPara(hwp);
        SetFontSize(hwp, 9);
        hwp.HAction.Run("MoveRight");
        BreakPara(hwp);
        hwp.HAction.Run("ParagraphShapeAlignJustify");
    }

    /// <summary>
    /// 금감보고서목차 헤더 — nested 9-col 띠 표. tool2 한컴라이브러리_decompiled.py:14716-14757 1:1.
    /// [0.1, 0.1, 0.1, 0.1, 32, 0.1, 0.1, 0.1, 0.1 mm × 9.3 mm].
    ///   좌 4 칸 — 짙은회색(127) → 라이트회색(216) 띠 (TableResizeExLeft 로 확장)
    ///   가운데 (32mm) — "목  차" HY헤드라인M 20pt
    ///   우 4 칸 — 좌측 대칭.
    /// </summary>
    private static void 금감보고서목차제목(dynamic hwp)
    {
        hwp.HAction.Run("ParagraphShapeAlignCenter");
        MakeTable(hwp,
            new[] { 0.1, 0.1, 0.1, 0.1, 32.0, 0.1, 0.1, 0.1, 0.1 },
            new[] { 9.3 });
        SetCellMarginZero(hwp);
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        hwp.HAction.Run("TableCellBlockExtend");
        SetTableBorderType(hwp, BorderType.None, BorderType.None, BorderType.None, BorderType.None);
        SetTableInnerLineType(hwp, BorderType.None, BorderType.None);
        hwp.HAction.Run("Cancel");
        hwp.MovePos(106, 0, 0);
        hwp.MovePos(104, 0, 0);
        SetTableBg(hwp, 127, 127, 127);
        MoveTableRight(hwp, 1);
        hwp.HAction.Run("TableResizeExLeft");
        MoveTableRight(hwp, 1);
        hwp.HAction.Run("TableResizeExLeft");
        SetTableBg(hwp, 216, 216, 216);
        MoveTableRight(hwp, 2);
        SetFontFace(hwp, "HY헤드라인M");
        SetFontSize(hwp, 20);
        hwp.HAction.Run("ParagraphShapeAlignCenter");
        InsertText(hwp, "목  차");
        SetTableBorderType(hwp, BorderType.Solid, BorderType.Solid, BorderType.Solid, BorderType.Solid);
        MoveTableRight(hwp, 2);
        SetTableBg(hwp, 216, 216, 216);
        hwp.HAction.Run("TableResizeExLeft");
        MoveTableRight(hwp, 1);
        hwp.HAction.Run("TableResizeExLeft");
        MoveTableRight(hwp, 1);
        SetTableBg(hwp, 127, 127, 127);
        hwp.HAction.Run("MoveRight");
    }

    // ────────────────────────────────────────────────────────────────────
    // 9~12. 기호 — 단순 InsertText 1~2 글자
    // ────────────────────────────────────────────────────────────────────
    public static void 당구장(dynamic hwp) => InsertText(hwp, "※");

    public static void 십자가(dynamic hwp) => InsertText(hwp, "†");

    /// <summary>본문 L1 글머리 — □ (U+25A1). 마크다운 변환 후 결과와 동일.</summary>
    public static void 네모(dynamic hwp) => InsertText(hwp, "□");

    /// <summary>본문 L2 글머리 입력용 — ○ (U+25CB). md 변환 시 출력은 ◦ 로 바뀜.</summary>
    public static void 동그라미(dynamic hwp) => InsertText(hwp, "○");

    /// <summary>본문 L2 글머리 출력용 — ◦ (U+25E6). 마크다운 변환 후 결과.</summary>
    public static void 작은동그라미(dynamic hwp) => InsertText(hwp, "◦");

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
        new(2, "헤더", h => 헤더_약식((dynamic)h),
            "헤더 (약식)", "상하 진남선 (#3E57A5) + HY헤드라인M 18pt 가운데"),
        new(3, "헤더", h => 금감원페이지중제목((dynamic)h),
            "중제목 (Ⅰ./Ⅱ.)", "파란밑줄 HY견명조+HY헤드라인M"),
        new(4, "헤더", h => 중제목_그라인드((dynamic)h),
            "중제목 (그라데이션)", "제목 + 하단 1.3mm 그라데이션 ribbon (#3333A0→#E3E2F2)"),
        new(5, "헤더", h => 금감원페이지소제목((dynamic)h),
            "소제목 (가./나.)", "라벤더 마커+본문 2셀"),
        // ── 목차 ──
        new(6, "목차", h => 금감보고서목차((dynamic)h),
            "목차 박스", "Ⅰ. 추진 배경 / Ⅱ. 추진 방향 / Ⅲ. 향후 계획 자동 키-인"),
        // ── 참고박스 ──
        new(7, "참고박스", h => 금감원페이지꺽쇠박스((dynamic)h),
            "꺽쇠박스", "〈◈◈◈ 관련 현황〉 3셀 헤더"),
        new(8, "참고박스", h => 금감원페이지점선박스((dynamic)h),
            "점선박스", "⇨ 결론 점선박스 민트"),
        new(9, "참고박스", h => 요약박스((dynamic)h),
            "요약박스", "◈ 마커 + 라벤더 배경 (#EBDEF1) + 한컴윤고딕 14.5pt"),
        new(10, "참고박스", h => 화살표박스_회색((dynamic)h),
            "화살표박스 (회색)", "➡ 마커 + 연회색 배경 (#F2F2F2) + 휴먼명조 15pt"),
        new(11, "참고박스", h => 참고박스_마크다운((dynamic)h),
            "참고박스 (마크다운 변환)", "md [참고] 점선박스 + ※(참고)"),
        new(12, "참고박스", h => 금감보고서블루진박스((dynamic)h),
            "블루진박스", "진남 헤더 + 12pt Bold + 본문"),
        // ── 붙임박스 ──
        new(13, "붙임박스", h => 금감원페이지참고((dynamic)h),
            "참고 (진파헤더)", "진파 라벨 + HY헤드라인M 15pt 본문"),
        // ── 기호 ──
        new(14, "기호", h => 당구장((dynamic)h),
            "당구장 ※", "주석 마커 ※ 1 글자"),
        new(15, "기호", h => 십자가((dynamic)h),
            "십자가 †", "보조 주석 마커 † 1 글자"),
        new(16, "기호", h => 네모((dynamic)h),
            "네모 □", "본문 L1 글머리 □ (마크다운 후 결과)"),
        new(17, "기호", h => 동그라미((dynamic)h),
            "동그라미 ○", "본문 L2 글머리 입력용 ○ (md 변환 시 ◦ 로 바뀜)"),
        new(18, "기호", h => 작은동그라미((dynamic)h),
            "작은 동그라미 ◦", "본문 L2 글머리 ◦ (마크다운 후 결과)"),
        new(19, "기호", h => 꺽쇠_홑((dynamic)h),
            "꺽쇠 「」 (홑)", "맑은 고딕 「」, 캐럿은 」 뒤"),
        new(20, "기호", h => 꺽쇠_겹((dynamic)h),
            "꺽쇠 『』 (겹)", "맑은 고딕 『』, 책·문헌 제목"),
    };
}
