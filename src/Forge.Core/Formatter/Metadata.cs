// YAML front-matter — 보고서 메타데이터 (3 필드 고정).
//
// Python 원본은 한국어 필드명 (보고서명/작성부서/작성일) 직접 사용. C# 도
// 한국어 식별자 합법이지만 IDE 자동완성·외부 노출 일관성 위해 영어 PascalCase
// + YamlMember alias 로 매핑.

using YamlDotNet.Serialization;

namespace Forge.Core.Formatter;

public sealed record Metadata
{
    [YamlMember(Alias = "보고서명")]
    public string? ReportTitle { get; init; }

    [YamlMember(Alias = "작성부서")]
    public string? Department { get; init; }

    [YamlMember(Alias = "작성일")]
    public string? Date { get; init; }  // YYYY-MM-DD
}
