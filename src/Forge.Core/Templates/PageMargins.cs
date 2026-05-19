// 문서 여백 (mm). Python 원본 PageMargins dataclass 1:1.
// 사용자 요청 기본: 좌우 20, 나머지 모두 10.

namespace Forge.Core.Templates;

public sealed record PageMargins
{
    public double Left   { get; init; } = 20.0;
    public double Right  { get; init; } = 20.0;
    public double Top    { get; init; } = 10.0;
    public double Bottom { get; init; } = 10.0;
    public double Header { get; init; } = 10.0;
    public double Footer { get; init; } = 10.0;
}
