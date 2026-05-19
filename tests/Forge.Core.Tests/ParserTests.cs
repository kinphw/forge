// Parser 단위 테스트 — Python 원본 동작과 동등성 확인.
// COM/한/글 무의존 — 순수 텍스트 처리만 검증.

using Forge.Core.Formatter;

namespace Forge.Core.Tests;

public class ParserTests
{
    [Fact]
    public void EmptyInput_YieldsEmptyDocument()
    {
        var doc = Parser.Parse("");
        Assert.Null(doc.Metadata.ReportTitle);
        Assert.Empty(doc.Nodes);
    }

    [Fact]
    public void FrontMatter_ParsesKoreanKeys()
    {
        var src = """
---
보고서명: 2026 1분기 영업실적
작성부서: 전략기획실
작성일: 2026-04-01
---
1. 개요
""";
        var doc = Parser.Parse(src);
        Assert.Equal("2026 1분기 영업실적", doc.Metadata.ReportTitle);
        Assert.Equal("전략기획실", doc.Metadata.Department);
        Assert.Equal("2026-04-01", doc.Metadata.Date);
        Assert.Single(doc.Nodes);
        Assert.Equal(NodeType.Section, doc.Nodes[0].Type);
    }

    [Fact]
    public void Section_And_Subsection_AreParsed()
    {
        var doc = Parser.Parse("1. 첫 섹션\n가. 첫 소제목\n");
        Assert.Equal(2, doc.Nodes.Count);
        Assert.Equal(NodeType.Section, doc.Nodes[0].Type);
        Assert.Equal("1.", doc.Nodes[0].Marker);
        Assert.Equal("첫 섹션", doc.Nodes[0].Text);
        Assert.Equal(NodeType.Subsection, doc.Nodes[1].Type);
        Assert.Equal("가.", doc.Nodes[1].Marker);
    }

    [Theory]
    [InlineData("□ L1 본문", "□")]
    [InlineData("○ L2 본문", "○")]
    [InlineData("- L3 본문", "-")]
    [InlineData("· L4 본문", "·")]
    [InlineData("ㅁ L1 한글 alias", "□")]   // 자모 alias → canonical
    [InlineData("ㅇ L2 한글 alias", "○")]
    public void Bullet_MarkersAndAliases(string line, string canonicalMarker)
    {
        var doc = Parser.Parse(line);
        Assert.Single(doc.Nodes);
        Assert.Equal(NodeType.Bullet, doc.Nodes[0].Type);
        Assert.Equal(canonicalMarker, doc.Nodes[0].Marker);
    }

    [Fact]
    public void Bullet_Summary_Extracted()
    {
        var doc = Parser.Parse("□ (요약) 핵심 내용");
        var n = doc.Nodes[0];
        Assert.Equal("요약", n.Summary);
        Assert.Equal("핵심 내용", n.Text);
    }

    [Fact]
    public void Annotation_Ref_And_General()
    {
        var doc = Parser.Parse("** 다중 참조 주석\n※ 일반 주석\n† 단검 주석");
        Assert.Equal(3, doc.Nodes.Count);
        Assert.All(doc.Nodes, n => Assert.Equal(NodeType.Annotation, n.Type));
        Assert.Equal("ref", doc.Nodes[0].AnnotationKind);
        Assert.Equal("**", doc.Nodes[0].Marker);
        Assert.Equal("general", doc.Nodes[1].AnnotationKind);
        Assert.Equal("※", doc.Nodes[1].Marker);
        Assert.Equal("general", doc.Nodes[2].AnnotationKind);
        Assert.Equal("†", doc.Nodes[2].Marker);
    }

    [Fact]
    public void Conclusion_Arrow()
    {
        var doc = Parser.Parse("=> 따라서 조치 필요");
        Assert.Single(doc.Nodes);
        Assert.Equal(NodeType.Conclusion, doc.Nodes[0].Type);
        Assert.Equal("따라서 조치 필요", doc.Nodes[0].Text);
    }

    [Fact]
    public void Callout_Note_CollectsChildrenUntilBlank()
    {
        var src = "[참고]\n□ 본문 1\n○ 본문 2\n\n다음 단락";
        var doc = Parser.Parse(src);
        Assert.Equal(NodeType.Callout, doc.Nodes[0].Type);
        Assert.Equal("note", doc.Nodes[0].CalloutKind);
        Assert.Equal(2, doc.Nodes[0].Children.Count);
    }

    [Fact]
    public void Callout_Attachment_WithNumber()
    {
        var src = "[붙임 3]\n본문 한 줄";
        var doc = Parser.Parse(src);
        Assert.Equal(NodeType.Callout, doc.Nodes[0].Type);
        Assert.Equal("attachment", doc.Nodes[0].CalloutKind);
        Assert.Equal(3, doc.Nodes[0].CalloutNumber);
    }

    [Fact]
    public void Table_Parsed_With_Aligns_And_Padding()
    {
        var src = """
| 항목 | 1분기 | 2분기 | 3분기 |
| :--- | ---: | :---: | --- |
| 매출 | 100 | 120 | 150 |
| 비용 | 80 |
""";
        var doc = Parser.Parse(src);
        Assert.Single(doc.Nodes);
        var t = doc.Nodes[0];
        Assert.Equal(NodeType.Table, t.Type);
        Assert.Equal(new[] { "항목", "1분기", "2분기", "3분기" }, t.Headers);
        Assert.Equal(new[] { "left", "right", "center", "left" }, t.Aligns);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(new[] { "매출", "100", "120", "150" }, t.Rows[0]);
        // T3 — 셀 부족 시 빈 문자열로 패딩
        Assert.Equal(new[] { "비용", "80", "", "" }, t.Rows[1]);
    }

    [Fact]
    public void BlankLines_Preserved()
    {
        var doc = Parser.Parse("1. 첫 섹션\n\n\n2. 두번째");
        Assert.Equal(4, doc.Nodes.Count);
        Assert.Equal(NodeType.Section, doc.Nodes[0].Type);
        Assert.Equal(NodeType.Blank, doc.Nodes[1].Type);
        Assert.Equal(NodeType.Blank, doc.Nodes[2].Type);
        Assert.Equal(NodeType.Section, doc.Nodes[3].Type);
    }
}
