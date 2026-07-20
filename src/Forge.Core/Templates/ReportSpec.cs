// 전체 보고서 양식 spec — 사용자가 GUI 에서 테일러링 가능.
//
// Python 원본 forge/formatter/templates.py 의 ReportSpec dataclass 1:1.
// 출처: reference/tool2/보고서1_spec.md (= tool2 금감원페이지 정확 spec).
//
// 기본값 = "보고서 1" (금감원페이지) — 표준 spec.
// 후속 작업으로 추가 (REPORT2_SPEC 등) — Python 과 동일하게 정적 팩토리로 노출.

namespace Forge.Core.Templates;

public sealed record ReportSpec
{
    public string Name { get; init; } = "보고서 1";
    public string Code { get; init; } = "report1";

    // ─ 페이지 설정 ─
    public PageMargins Margins { get; init; } = new();
    public int LineSpacingDefault { get; init; } = 150;  // 본문·제목·stamp 모두 일괄 150%

    // 비빈 노드 사이 자동 prepend 되는 빈 단락의 글자 크기 (pt)
    // — hotkey D(var_blank_size) 와 동일 의미. 변환 시점에 SSOT 주입으로 override 됨.
    public double BlankParaPt { get; init; } = 8.0;

    // 글머리 마커 직후 `(요약)` prefix 강조 폰트 — hotkey G(var_font4) SSOT.
    // 크기는 bullet level 의 SizePt 그대로 따라감 (현재 4단계 모두 15pt).
    public string BulletSummaryFont { get; init; } = "HY울릉도M";

    // ─ 대제목 (노란 박스) ─
    public string TitleFont          { get; init; } = "HY헤드라인M";
    public double TitleSizePt        { get; init; } = 17.0;
    public Rgb    TitleBgRgb         { get; init; } = new(250, 250, 191);  // 연노랑
    public double TitleBoxHeightMm   { get; init; } = 10.5;
    public int    TitleBorderThickness { get; init; } = 6;

    // ─ 부서·일자 stamp (우정렬) ─
    public string DateFont   { get; init; } = "휴먼명조";
    public double DateSizePt { get; init; } = 12.0;

    // ─ 중제목 (Ⅰ./Ⅱ.) ─
    public string SectionNumberFont    { get; init; } = "HY견명조";
    public double SectionNumberSizePt  { get; init; } = 15.0;
    public bool   SectionNumberBold    { get; init; } = true;
    public string SectionTitleFont     { get; init; } = "HY헤드라인M";
    public double SectionTitleSizePt   { get; init; } = 16.0;
    public Rgb    SectionUnderlineRgb  { get; init; } = new(0, 0, 255);    // 파란
    public double SectionBoxHeightMm   { get; init; } = 8.4;

    // ─ 소제목 (가/나/[1]/[2]) ─
    public string SubsectionFont           { get; init; } = "HY헤드라인M";
    public double SubsectionMarkerSizePt   { get; init; } = 15.0;
    public double SubsectionContentSizePt  { get; init; } = 15.5;
    public Rgb    SubsectionMarkerBgRgb    { get; init; } = new(224, 229, 250);  // 라벤더
    public Rgb    SubsectionBorderRgb      { get; init; } = new(62, 87, 165);    // 진파랑
    public double SubsectionBoxHeightMm    { get; init; } = 8.7;
    public double SubsectionMarkerWidthMm  { get; init; } = 7.5;
    public double SubsectionContentWidthMm { get; init; } = 150.0;  // 충분히 넓게 — tool2 의 49mm 확장

    // ─ 본문 글머리 4단계 (□ ○ - ·) ─
    // 모두 휴먼명조 15pt 동일, 깊이만 균등 누진:
    //   내어쓰기: -22 → -33.6 → -45.2 → -56.8 (Δ-11.6)
    //   fixed_pre: 1 → 3 → 5 → 7 (왼쪽 들여쓰기 2칸씩)
    // SpaceAbovePt 는 1.4부터 사용 안 함 (자동 빈 줄 삽입 알고리즘 제거).
    public IReadOnlyList<BulletStyle> Bullets { get; init; } = new BulletStyle[]
    {
        new() { MdGlyph = "□", OutGlyph = "□", Font = "휴먼명조", SizePt = 15.0,
                IndentPt = -22.0, FixedPre = 1, FixedPost = 2 },
        new() { MdGlyph = "○", OutGlyph = "◦", Font = "휴먼명조", SizePt = 15.0,
                IndentPt = -33.6, FixedPre = 3, FixedPost = 2 },
        new() { MdGlyph = "-", OutGlyph = "-", Font = "휴먼명조", SizePt = 15.0,
                IndentPt = -45.2, FixedPre = 5, FixedPost = 2 },
        new() { MdGlyph = "·", OutGlyph = "·", Font = "휴먼명조", SizePt = 15.0,
                IndentPt = -56.8, FixedPre = 7, FixedPost = 2 },
    };

    // ─ 주석 (단일 spec) ─
    // *, ※(당구장), †(십자가) 3종 모두 동일 처리. 맑은 고딕 12pt.
    // OutGlyph = "" 는 "입력 마커 그대로 보존" 의미.
    public BulletStyle Annotation { get; init; } = new()
    {
        MdGlyph = "*",
        OutGlyph = "",
        Font = "맑은 고딕",
        SizePt = 12.0,
        IndentPt = -33.6,
        FixedPre = 8,
        FixedPost = 2,
    };

    // ─ 결론 화살표 박스 (=>) ─
    //   기본 시각 = 양식삽입 #10 "화살표박스 (회색)" (reference borderFillIDRef="8").
    //   2026-07-16 사용자 요청으로 기존 민트 점선박스에서 교체 — ➡ 마커 + 연회색 배경 +
    //   얇은 검정 실선. 민트 점선이 필요하면 spec 에서 Bg/Dotted/Marker 를 되돌리면 됨.
    public string ConclusionFont          { get; init; } = "휴먼명조";
    public double ConclusionSizePt        { get; init; } = 15.0;
    public string ConclusionMarker        { get; init; } = "➡";                 // 이전: ⇨
    public Rgb    ConclusionBgRgb         { get; init; } = new(242, 242, 242);  // #F2F2F2 연회색
    public double ConclusionBoxHeightMm   { get; init; } = 17.0;
    public bool   ConclusionBorderDotted  { get; init; } = false;               // 실선
    public int    ConclusionBorderWidth   { get; init; } = 1;                   // 1 ≈ 0.12mm
    public Rgb    ConclusionBorderRgb     { get; init; } = new(0, 0, 0);

    // ─ 참고 callout ─
    public string NoteHeaderFont     { get; init; } = "HY헤드라인M";
    public double NoteHeaderSizePt   { get; init; } = 16.0;
    public Rgb    NoteHeaderBgRgb    { get; init; } = new(35, 35, 106);    // 진남색
    public Rgb    NoteHeaderTextRgb  { get; init; } = new(255, 255, 255);  // 흰색
    public double NoteHeaderWidthMm  { get; init; } = 18.0;
    public double NoteBoxHeightMm    { get; init; } = 10.0;
    public double NoteBodySizePt     { get; init; } = 20.0;

    // ─ 붙임 — 정예병 예시(ref/1.hwp) 붙임1 박스 실측 재현 ─
    //   라벨 배경 #333399(51,51,153) — Forge.Probe tabledump 로 SelCellsBorderFill.FillAttr
    //   .WinBrushFaceColor 실측 (기존 순수 파랑 0,0,255 에서 교체). 폰트 HY헤드라인M 15pt.
    public Rgb    AttachHeaderBgRgb  { get; init; } = new(51, 51, 153);   // #333399
    public double AttachHeaderSizePt { get; init; } = 15.0;
    public double AttachHeaderWidthMm{ get; init; } = 22.0;
    public double AttachBodySizePt   { get; init; } = 13.0;

    // ─ 표 (TableRenderer) ─
    public TableStyle Table { get; init; } = new();

    // 일부 필드만 바꾼 사본은 record `with` 식으로:
    //   var custom = ReportSpec.Report1 with { TitleFont = "맑은 고딕", TitleSizePt = 18.0 };

    // ────────────────────────────────────────────────────────────────────
    // 표준 spec 인스턴스
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 보고서 1 = 금감원페이지 표준 spec.
    /// 출처: reference/tool2/보고서1_spec.md (tool2 코드에서 직접 추출).
    /// </summary>
    public static readonly ReportSpec Report1 = new();

    // 향후 추가:
    // public static readonly ReportSpec Report2 = new() { Name = "보고서 2 (금감보고서)", ... };
    // public static readonly ReportSpec BusinessInfo = new() { Name = "업무정보", ... };
    // public static readonly ReportSpec PressRelease = new() { Name = "보도자료", ... };
}
