// 글머리 1단계 spec — tool2 금감원글머리지정 11속성과 1:1 대응.
// 주석에서도 동일 spec 사용 (단일 spec).

namespace Forge.Core.Templates;

public sealed record BulletStyle
{
    public required string MdGlyph   { get; init; }   // md 입력 글머리 (□ ○ - ·)
    public required string OutGlyph  { get; init; }   // 출력 글머리 (□ ◦ - ·) — 빈 문자열은 "marker 보존" 의미 (주석용)
    public required string Font      { get; init; }   // 폰트명 (휴먼명조, 맑은 고딕 등)
    public required double SizePt    { get; init; }   // 글자 크기 pt
    public required double IndentPt  { get; init; }   // 내어쓰기 (음수 가능)
    public bool   Bold          { get; init; } = false;
    public double SpaceAbovePt  { get; init; } = 0.0; // 위 빈 줄 크기 (pt) — 현재 사용 안 함 (1.4 자동 빈줄 제거)
    public int    LineSpacing   { get; init; } = 150; // 줄간격 %
    public int    FixedPre      { get; init; } = 0;   // 글머리 앞 InsertFixedWidthSpace 횟수
    public int    FixedPost     { get; init; } = 0;   // 글머리 뒤 InsertFixedWidthSpace 횟수
}
