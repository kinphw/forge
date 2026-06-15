// 개조식 markdown 파서.
//
// Python 원본 forge/formatter/parser.py 의 1:1 포팅.
//
// spec/markdown-spec.md v1.4 기준:
//   - YAML front-matter (보고서명·작성부서·작성일)
//   - 6단계 본문 층위 (1./가./□/○/-/·)
//   - 요약단어 ((요약))
//   - 주석 (* 참조 / ※ 일반)
//   - 강조 (__X__)  ← 인라인. 파서는 raw 문자열로 보존, 렌더러가 처리.
//   - 결론 화살표 (=>)
//   - Callout 박스 ([참고], [붙임]/[붙임 N])
//   - GFM 부분집합 표
//
// 출력은 시각 spec 무관한 추상 트리 (MarkdownDocument).

using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Forge.Core.Formatter;

public static class Parser
{
    // ──────────────────────────────────────────────────────────────────────
    // 정규식 — Python 원본과 동일 패턴
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Regex FrontMatterRegex =
        new(@"^---\s*\n(.*?)\n---\s*\n", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex SectionRegex =
        new(@"^(\d+)\.\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex SubsectionRegex =
        new(@"^([가나다라마바사아자차카타파하])\.\s+(.+)$", RegexOptions.Compiled);

    // 본문 글머리 — `□`/`○` 는 IME 직접 입력이 까다로움 (한자키 + 선택). 사용자가
    // 손으로 타이핑할 때 자주 쓰는 한글 자모 `ㅁ`(U+3141)/`ㅇ`(U+3147) 도 alias 로
    // 허용. 매칭 후 canonical(`□`/`○`) 저장 — 렌더러는 한 종류만 처리.
    private static readonly (string Marker, Regex Pattern)[] BulletPatterns =
    {
        ("□", new Regex(@"^[□ㅁ]\s*(.+)$", RegexOptions.Compiled)),
        ("○", new Regex(@"^[○ㅇ]\s*(.+)$", RegexOptions.Compiled)),
        ("-", new Regex(@"^-\s+(.+)$",     RegexOptions.Compiled)),
        ("·", new Regex(@"^·\s*(.+)$",     RegexOptions.Compiled)),
    };

    private static readonly Regex SummaryRegex =
        new(@"^\(([^)]+)\)\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex ConclusionRegex =
        new(@"^=>\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex AnnotationRefRegex =
        new(@"^(\*+)\s+(.+)$", RegexOptions.Compiled);
    // ※(당구장) 또는 †(십자가)
    private static readonly Regex AnnotationGenRegex =
        new(@"^[※†]\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex CalloutNoteRegex =
        new(@"^\[참고\]\s*$", RegexOptions.Compiled);
    private static readonly Regex CalloutAttachRegex =
        new(@"^\[붙임(?:\s+(\d+))?\]\s*$", RegexOptions.Compiled);

    // GFM 표 — 행은 `|...|` (양끝 `|` 필수), 구분선은 `|---|---|...|` 또는 정렬 표기
    private static readonly Regex TableRowRegex =
        new(@"^\s*\|.+\|\s*$", RegexOptions.Compiled);
    // 구분선 셀은 하이픈 1개 이상 (`:?-+:?`). GFM 표준은 셀당 하이픈 최소 1개를 허용하며,
    // prettier 등 포매터가 헤더 폭에 맞춰 `--`(2개)로 줄이는 경우가 흔함 — `-{3,}` 는 그런
    // 표를 인식 못 해 일반 텍스트로 떨어뜨림 (사용자 보고 2026-06-15).
    private static readonly Regex TableSepRegex =
        new(@"^\s*\|(\s*:?-+:?\s*\|)+\s*$", RegexOptions.Compiled);

    // ──────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 개조식 md 텍스트 → MarkdownDocument.
    ///
    /// 동작:
    ///   1. YAML front-matter 추출
    ///   2. 본문을 라인 단위로 스캔
    ///   3. 라인 시작 글머리/마커 패턴 매칭 → Node 생성
    ///   4. callout 시작 마커 만나면 빈 줄까지 children 으로 수집
    /// </summary>
    public static MarkdownDocument Parse(string src)
    {
        src = src.TrimStart('﻿');  // BOM 제거
        var (metadata, body) = SplitFrontMatter(src);
        var nodes = ParseBody(body);
        return new MarkdownDocument(metadata, nodes);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Front-matter
    // ──────────────────────────────────────────────────────────────────────

    private static (Metadata Metadata, string Body) SplitFrontMatter(string src)
    {
        var m = FrontMatterRegex.Match(src);
        if (!m.Success) return (new Metadata(), src);

        Metadata metadata;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(NullNamingConvention.Instance)  // YamlMember alias 매칭
                .IgnoreUnmatchedProperties()
                .Build();
            metadata = deserializer.Deserialize<Metadata>(m.Groups[1].Value) ?? new Metadata();
        }
        catch (YamlDotNet.Core.YamlException)
        {
            metadata = new Metadata();
        }
        return (metadata, src[m.Length..]);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 본문
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Python <c>str.splitlines()</c> 와 동등.
    /// 차이점: C# <c>string.Split('\n')</c> 는 "" → [""] / "a\n" → ["a",""] 로
    /// trailing 빈 entry 를 만들지만 Python splitlines 는 안 만듦. 마지막이
    /// 줄바꿈으로 끝나면 trailing 빈 entry 제거하여 동등성 확보.
    /// </summary>
    private static string[] SplitLines(string s)
    {
        if (string.IsNullOrEmpty(s)) return Array.Empty<string>();
        var lines = s.Split('\n');
        if (lines.Length > 0 && s.EndsWith('\n') && lines[^1].Length == 0)
            return lines[..^1];
        return lines;
    }

    private static List<Node> ParseBody(string body)
    {
        var nodes = new List<Node>();
        var lines = SplitLines(body);
        // 각 라인에서 trim 처리 — trailing \r (CRLF) 제거.

        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].Trim();

            if (line.Length == 0)
            {
                nodes.Add(new Node { Type = NodeType.Blank });
                i++;
                continue;
            }

            // callout 시작?
            if (CalloutNoteRegex.IsMatch(line))
            {
                var (children, consumed) = ParseCalloutBody(lines, i + 1);
                var node = new Node { Type = NodeType.Callout, CalloutKind = "note" };
                node.Children.AddRange(children);
                nodes.Add(node);
                i += 1 + consumed;
                continue;
            }
            var mAttach = CalloutAttachRegex.Match(line);
            if (mAttach.Success)
            {
                int? num = mAttach.Groups[1].Success ? int.Parse(mAttach.Groups[1].Value) : null;
                // ★ [붙임] 박스 본문은 다음 1줄만 — 그 뒤 줄들은 box 밖 일반 본문으로 흐름.
                //   ([참고] 는 기존대로 빈 줄까지 multi-line.)
                var (children, consumed) = ParseCalloutBody(lines, i + 1, maxLines: 1);
                var node = new Node
                {
                    Type = NodeType.Callout,
                    CalloutKind = "attachment",
                    CalloutNumber = num,
                };
                node.Children.AddRange(children);
                nodes.Add(node);
                i += 1 + consumed;
                continue;
            }

            // 표 — `|...|` + 다음 줄이 구분선이면 표 시작.
            if (TableRowRegex.IsMatch(line) && i + 1 < lines.Length)
            {
                var nextLine = lines[i + 1].Trim();
                if (TableSepRegex.IsMatch(nextLine))
                {
                    var (tableNode, consumed) = ParseTable(lines, i);
                    nodes.Add(tableNode);
                    i += consumed;
                    continue;
                }
            }

            // 단일 라인 노드들
            var single = ParseSingleLine(line);
            if (single is not null)
            {
                nodes.Add(single);
            }
            else
            {
                // 미식별 라인 — 그냥 텍스트로 (마커 없는 본문)
                nodes.Add(new Node { Type = NodeType.Bullet, Marker = null, Text = line });
            }
            i++;
        }
        return nodes;
    }

    private static (List<Node> Children, int Consumed) ParseCalloutBody(
        string[] lines, int start, int maxLines = int.MaxValue)
    {
        var children = new List<Node>();
        int j = start;
        while (j < lines.Length && children.Count < maxLines)
        {
            var line = lines[j].Trim();
            if (line.Length == 0) break;
            // callout 안의 다른 callout / 섹션은 금지 (spec §8) — 만나면 종료
            if (CalloutNoteRegex.IsMatch(line) || CalloutAttachRegex.IsMatch(line)) break;
            if (SectionRegex.IsMatch(line)) break;

            var node = ParseSingleLine(line);
            if (node is not null)
                children.Add(node);
            else
                children.Add(new Node { Type = NodeType.Bullet, Marker = null, Text = line });
            j++;
        }
        return (children, j - start);  // 빈 줄 자체는 외부 루프에서 소비
    }

    private static Node? ParseSingleLine(string line)
    {
        // 결론 화살표
        var m = ConclusionRegex.Match(line);
        if (m.Success)
            return new Node { Type = NodeType.Conclusion, Marker = "=>", Text = m.Groups[1].Value };

        // 일반 주석 ※(당구장) 또는 †(십자가)
        m = AnnotationGenRegex.Match(line);
        if (m.Success)
            return new Node
            {
                Type = NodeType.Annotation,
                Marker = line[..1],   // 입력 마커 보존
                Text = m.Groups[1].Value,
                AnnotationKind = "general",
            };

        // 참조 주석 *  (단, "- " 와 충돌 안 함 — 위 분기에서 처리됨)
        m = AnnotationRefRegex.Match(line);
        if (m.Success && !line.StartsWith("- "))
            return new Node
            {
                Type = NodeType.Annotation,
                Marker = m.Groups[1].Value,
                Text = m.Groups[2].Value,
                AnnotationKind = "ref",
            };

        // 섹션
        m = SectionRegex.Match(line);
        if (m.Success)
            return new Node
            {
                Type = NodeType.Section,
                Marker = $"{m.Groups[1].Value}.",
                Text = m.Groups[2].Value,
            };

        // 소제목
        m = SubsectionRegex.Match(line);
        if (m.Success)
            return new Node
            {
                Type = NodeType.Subsection,
                Marker = $"{m.Groups[1].Value}.",
                Text = m.Groups[2].Value,
            };

        // 본문 글머리
        foreach (var (marker, pattern) in BulletPatterns)
        {
            m = pattern.Match(line);
            if (!m.Success) continue;

            var text = m.Groups[1].Value.Trim();
            // 마커 뒤 `(...)` prefix 가 있으면 summary 로 분리 — L1~L4 동일.
            string? summary = null;
            var ms = SummaryRegex.Match(text);
            if (ms.Success)
            {
                summary = ms.Groups[1].Value;
                text = ms.Groups[2].Value;
            }
            return new Node
            {
                Type = NodeType.Bullet,
                Marker = marker,
                Text = text,
                Summary = summary,
            };
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────
    // 표 (GFM 부분집합)
    // ──────────────────────────────────────────────────────────────────────

    private static List<string> SplitRow(string line)
    {
        // `| a | b | c |` → ['a', 'b', 'c']. 양끝 `|` 제거 + 내부 split + 셀 trim.
        var inner = line.Trim().Trim('|');
        return inner.Split('|').Select(c => c.Trim()).ToList();
    }

    private static List<string> ParseAligns(string sepLine)
    {
        // `|:---|---:|:---:|---|` → ['left','right','center','left'].
        var cells = SplitRow(sepLine);
        var result = new List<string>(cells.Count);
        foreach (var c in cells)
        {
            var left = c.StartsWith(':');
            var right = c.EndsWith(':');
            result.Add((left, right) switch
            {
                (true, true)  => "center",
                (false, true) => "right",
                _             => "left",
            });
        }
        return result;
    }

    /// <summary>
    /// 헤더(start) + 구분선(start+1) + 데이터행 N개 → Node(Type=Table).
    /// 종료 조건: 빈 줄 / `|` 로 시작하지 않는 줄 / 문서 끝.
    /// 데이터 행 셀 수 &lt; 헤더 → 빈 문자열로 패딩 (T3).
    /// 데이터 행 셀 수 &gt; 헤더 → 초과 셀은 슬라이스로 무시 (T4) — dispatcher 가 log 경고.
    /// </summary>
    private static (Node Node, int Consumed) ParseTable(string[] lines, int start)
    {
        var headers = SplitRow(lines[start]);
        var aligns = ParseAligns(lines[start + 1]);
        var ncols = headers.Count;
        var rows = new List<List<string>>();
        int j = start + 2;
        while (j < lines.Length)
        {
            var line = lines[j].Trim();
            if (line.Length == 0 || !TableRowRegex.IsMatch(line)) break;
            var cells = SplitRow(line);
            if (cells.Count < ncols)
                cells.AddRange(Enumerable.Repeat("", ncols - cells.Count));
            else if (cells.Count > ncols)
                cells = cells.Take(ncols).ToList();
            rows.Add(cells);
            j++;
        }
        var node = new Node { Type = NodeType.Table };
        node.Headers.AddRange(headers);
        node.Rows.AddRange(rows);
        node.Aligns.AddRange(aligns);
        return (node, j - start);
    }
}
