// 파싱된 단락 한 개. Python 원본 forge/formatter/parser.py 의 Node dataclass 1:1.
//
// 의도적으로 mutable class — 표 노드의 headers/rows/aligns 처럼 파서가 후속
// 채워 넣는 필드가 있고, 자식 callout 도 children 에 append 한다. C# 컨벤션상
// init-only record 가 더 깔끔하지만, 1:1 포팅과 파서 구현의 직관성을 위해
// Python 의 dataclass 그대로 mutable 유지.

namespace Forge.Core.Formatter;

public enum NodeType
{
    Section,      // 1. 섹션 헤더
    Subsection,   // 가. 소제목
    Bullet,       // □ ○ - · 본문 글머리
    Annotation,   // * 참조 / ※ 일반 주석
    Conclusion,   // => 결론 화살표
    Callout,      // [참고] / [붙임] 박스
    Blank,        // 빈 줄 (구조 유지용)
    Table,        // GFM 표 (헤더 + 구분선 + 데이터 N행)
}

public sealed class Node
{
    public NodeType Type { get; init; }
    public string Text { get; set; } = "";
    public string? Marker { get; set; }
    public string? Summary { get; set; }

    // callout 전용
    public string? CalloutKind { get; set; }    // "note" | "attachment"
    public int? CalloutNumber { get; set; }     // [붙임 1], [붙임 2] ...
    public List<Node> Children { get; init; } = new();

    // annotation 전용
    public string? AnnotationKind { get; set; } // "ref" (*/**) | "general" (※/†)

    // table 노드 전용 (다른 type 에선 빈 리스트로 무시)
    public List<string> Headers { get; init; } = new();
    public List<List<string>> Rows { get; init; } = new();
    public List<string> Aligns { get; init; } = new();  // "left" | "center" | "right"
}
