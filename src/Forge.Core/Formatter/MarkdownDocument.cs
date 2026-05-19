// 파싱 완료된 md 전체.

namespace Forge.Core.Formatter;

public sealed record MarkdownDocument(Metadata Metadata, IReadOnlyList<Node> Nodes);
