// 표 렌더러 spec — tool2 `행안부초록표` (line 3053+) 권위 패턴 + subsection 색 일관.
//
// 1열(라벨) = 25mm 고정, 나머지 (N-1) 열 = (usable_width - 25) / (N-1) 균등.
// 셀 padding = 0 (tool2 `셀여백제로` 권위). 데이터 셀 폰트는 본문 15pt 대비
// 한 단계 축소된 12pt — 산출물 4열 표(셀 폭 ≈47mm) 에서 가독성·페이지 수용 균형.

namespace Forge.Core.Templates;

public sealed record TableStyle
{
    public double LabelColMm     { get; init; } = 25.0;
    public Rgb    BorderColor    { get; init; } = new(80, 80, 80);   // 진회색
    public int    BorderThick    { get; init; } = 6;
    public bool   HideSideBorders{ get; init; } = true;              // 외곽 좌·우만 없음 처리 (상·하·내부선 유지)
    public Rgb    HeaderBg       { get; init; } = new(242, 242, 242);// 한컴 팔레트 "하양 5% 어둡게"
    public string HeaderFont     { get; init; } = "맑은 고딕";
    public double HeaderSizePt   { get; init; } = 12.0;
    public bool   HeaderBold     { get; init; } = true;              // 헤더만 Bold
    public string BodyFont       { get; init; } = "맑은 고딕";
    public double BodySizePt     { get; init; } = 12.0;
    public double RowHeightMm    { get; init; } = 8.4;               // subsection_box_height_mm 와 일관

    // GFM aligns 가 모두 default("left", = `---`) 일 때 본 값으로 override.
    // `:---`/`---:`/`:---:` 명시 시 그대로 사용.
    public string BodyAlign      { get; init; } = "center";
    public int    BodyLineSpacing{ get; init; } = 130;               // 문단 행간 (%)

    // 한/글 셀당 시각 폭 inflate 보정 (mm). 진단: 한/글이 우리 ColWidth 와
    // 별개로 셀당 +3.67mm (좌+우 default cell padding ≈ 1.8mm × 2) 를 시각
    // 폭에 추가 — make_table 호출 시 각 셀에서 미리 빼야 의도와 일치.
    public double CellInflationMm{ get; init; } = 3.67;
    public double WidthSafetyMm  { get; init; } = 0.0;               // 외곽선 굵기 등 미세 보정
}
