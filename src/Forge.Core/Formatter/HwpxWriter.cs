// MarkdownDocument → .hwpx 파일 생성 (또는 활성 문서 커서 위치 삽입).
//
// Python 원본 forge/formatter/hwpx_writer.py 의 dispatcher 포팅.
// 실제 시각 렌더링은 Forge.Core.Renderers 의 각 ElementRenderer 가 담당.
//
// Mode:
//   - New    : FileNew → 페이지 여백 → 메타데이터 헤더 → 본문 → SaveAs
//   - Cursor : 활성 문서·커서 위치 그대로. 본문만 삽입. 저장은 사용자가 한/글에서.
//
// ★ STAGE 2 후처리 (linter — 들여쓰기/자간 정렬) 는 W3 에서 추가. 현재는
//   ApplyIndentAlign / ApplyKerning 인자만 받아두고 실제 호출은 NoOp.

using Forge.Core.Renderers;
using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Formatter;

public enum HwpxWriteMode { New, Cursor }

public sealed class NoSelectionException : Exception
{
    public NoSelectionException(string message) : base(message) { }
    public NoSelectionException(string message, Exception inner) : base(message, inner) { }
}

public static class HwpxWriter
{
    public delegate void LogFn(string message);

    // md 글머리 → bullet level 매핑
    private static readonly Dictionary<string, int> BulletLevels = new()
    {
        ["□"] = 1, ["○"] = 2, ["-"] = 3, ["·"] = 4,
    };

    /// <summary>
    /// md 변환 진입점.
    /// </summary>
    /// <param name="hwp">살아있는 한/글 COM 인스턴스 (HwpSession.Hwp).</param>
    /// <param name="doc">파싱된 markdown.</param>
    /// <param name="outPath">저장할 .hwpx 절대 경로 (New 모드 + 즉시 저장).
    ///   null/빈 문자열 → 저장 단계 skip — caller 가 SaveAsHwpx 직접 호출.</param>
    /// <param name="spec">보고서 양식 spec. null 이면 Report1.</param>
    /// <param name="log">진행 로그 콜백. null 이면 무시.</param>
    /// <param name="mode">New | Cursor.</param>
    /// <param name="department">spec v1.4 — UI 입력의 작성부서.</param>
    /// <param name="date">spec v1.4 — UI 입력의 작성일 (YYYY-MM-DD).</param>
    /// <param name="applyIndentAlign">STAGE 2 들여쓰기 정렬 (W3 에서 활성화).</param>
    /// <param name="applyKerning">STAGE 2 자간조정 (W3 에서 활성화).</param>
    /// <returns>New + outPath: 저장 경로 / 그 외: 빈 문자열.</returns>
    public static string GenerateHwpxViaCom(
        dynamic hwp,
        MarkdownDocument doc,
        string? outPath = null,
        ReportSpec? spec = null,
        LogFn? log = null,
        HwpxWriteMode mode = HwpxWriteMode.New,
        string? department = null,
        string? date = null,
        bool applyIndentAlign = true,
        bool applyKerning = true)
    {
        spec ??= ReportSpec.Report1;
        log ??= _ => { };

        bool metadataEmitted = false;

        if (mode == HwpxWriteMode.New)
        {
            outPath = string.IsNullOrEmpty(outPath) ? "" : Path.GetFullPath(outPath);

            // 운영 정책: 항상 기존 attach 케이스 (allow_spawn=false) → FileNew 로 분리.
            // XHwpDocuments.Add() 는 HAction cursor target 이 이전 doc 에 남는 부작용
            // (사용자 검증 2026-04-27) 있어 사용 금지.
            log("[STAGE 1] 새 문서 생성 (FileNew)");
            Run(hwp, "FileNew");

            var m = spec.Margins;
            log($"[STAGE 1] 페이지 여백: L={m.Left} R={m.Right} T={m.Top} B={m.Bottom} mm");
            SetPageMargins(hwp, m.Left, m.Right, m.Top, m.Bottom, m.Header, m.Footer);

            // 메타데이터 헤더 (보고서명 노란박스 + 부서·일자 stamp)
            var meta = doc.Metadata;
            if (!string.IsNullOrEmpty(meta.ReportTitle)
                || !string.IsNullOrEmpty(department)
                || !string.IsNullOrEmpty(date))
            {
                log($"[STAGE 1] 메타데이터 헤더: 보고서명={meta.ReportTitle.Repr()} 부서={department.Repr()} 일자={date.Repr()}");
                new MetadataRenderer(hwp, spec).Render(meta.ReportTitle, department, date);
                metadataEmitted = true;
            }
        }
        else
        {
            log("[STAGE 1] 활성 문서 커서 위치에 본문 삽입 (페이지·메타데이터 미변경)");
        }

        // ─── 본문 노드 dispatcher ───
        log($"[STAGE 1] 본문 {doc.Nodes.Count} 노드 dispatcher 시작");
        DispatchNodes(hwp, doc.Nodes, spec, log, initialPrevEmitted: metadataEmitted);

        // ─── STAGE 2 후처리 (new 모드만) ───
        // 순서: 들여쓰기 → 자간 → 들여쓰기 (3단계). hotkey Q '자동 정렬' 동일 패턴.
        // 검증 (2026-05-06): 인덴트 0 상태에서 자간 → 인덴트 시 wrap 옆 밀려 자간
        // 보정 무효화. 인덴트 → 자간 만 시 자간 후 drift 로 인덴트 어긋남.
        // 1차 인덴트로 wrap 기준 고정 → 자간 → 2차 인덴트 drift 보정.
        // cursor 모드는 skip — 기존 문서 뒤 추가 시나리오 보호.
        if (mode == HwpxWriteMode.New)
        {
            // HwpxWriter.LogFn 과 Linter.LogFn 이 이름 충돌 — 람다로 brige.
            Linter.LogFn linterLog = msg => log("  " + msg);

            if (applyIndentAlign)
            {
                log("[STAGE 2] (1/3) 들여쓰기 정렬 — wrap 기준 확정");
                try { Linter.IndentAlign.AlignLeftIndent(hwp, linterLog); }
                catch (Exception e) { log($"  ⚠ 1차 들여쓰기 정렬 중단: {e.Message}"); }
            }
            if (applyKerning)
            {
                log("[STAGE 2] (2/3) 자간조정 — 어절 잘림 방지 (줄당 ±15회)");
                try { Linter.Kerning.AdjustKerningToAvoidWordBreak(hwp, linterLog); }
                catch (Exception e) { log($"  ⚠ 자간조정 중단: {e.Message}"); }
            }
            if (applyIndentAlign)
            {
                log("[STAGE 2] (3/3) 들여쓰기 재정렬 — 자간 drift 보정");
                try { Linter.IndentAlign.AlignLeftIndent(hwp, linterLog); }
                catch (Exception e) { log($"  ⚠ 2차 들여쓰기 정렬 중단: {e.Message}"); }
            }
        }

        // ─── 모드별 저장 ───
        if (mode == HwpxWriteMode.New)
        {
            if (!string.IsNullOrEmpty(outPath))
            {
                log($"[STAGE 1] hwpx 저장: {outPath}");
                SaveAsHwpx(hwp, outPath);
                log("[STAGE 1] ✔ 완료");
                return outPath;
            }
            log("[STAGE 1] ✔ 변환 완료 — 저장은 caller 책임 (지연-저장)");
            return "";
        }

        log("[STAGE 1] ✔ 커서 위치 삽입 완료 (저장은 한/글에서 직접)");
        return "";
    }

    // ────────────────────────────────────────────────────────────────────
    // 노드 → 렌더러 dispatcher
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 노드 리스트 순회 — 타입에 따라 적절한 렌더러 호출.
    ///
    /// ★ Blank 처리 정책 (2026-04-30):
    ///   모든 비빈 노드 사이에 정확히 1줄 8pt 빈 단락 보장 (consistent prepend).
    ///   - md 소스 빈 줄 없어도 자동 1줄 삽입 → 모든 변형 사이 일관 spacing
    ///   - md 소스 빈 줄 1+개 있어도 1줄로 coalesce
    ///   - 첫 노드 앞 / 마지막 노드 뒤 자동 삽입 안 함
    /// </summary>
    private static void DispatchNodes(
        dynamic hwp,
        IReadOnlyList<Node> nodes,
        ReportSpec spec,
        LogFn log,
        bool initialPrevEmitted)
    {
        void EmitBlankPara()
        {
            try
            {
                SetFontSize(hwp, spec.BlankParaPt);
                BreakPara(hwp);
            }
            catch { /* 폰트 적용 실패 무시 — break_para 만이라도 시도 */ }
        }

        bool lastWasEmit = initialPrevEmitted;
        foreach (var node in nodes)
        {
            if (node.Type == NodeType.Blank)
            {
                if (lastWasEmit)
                {
                    EmitBlankPara();
                    lastWasEmit = false;
                }
                continue;
            }
            if (lastWasEmit) EmitBlankPara();

            try
            {
                DispatchOne(hwp, node, spec);
                lastWasEmit = true;
            }
            catch (Exception e)
            {
                log($"  ✘ 노드 렌더링 실패 ({node.Type} marker={node.Marker.Repr()}): {e.Message}");
                try { BreakPara(hwp); } catch { }
                lastWasEmit = true;
            }
        }
    }

    private static void DispatchOne(dynamic hwp, Node node, ReportSpec spec)
    {
        switch (node.Type)
        {
            case NodeType.Section:
            {
                int num = 0;
                if (node.Marker is not null && int.TryParse(node.Marker.TrimEnd('.'), out var parsed))
                    num = parsed;
                new SectionRenderer(hwp, spec).Render(num, node.Text);
                return;
            }
            case NodeType.Subsection:
            {
                var marker = node.Marker?.TrimEnd('.') ?? "";
                new SubsectionRenderer(hwp, spec).Render(marker, node.Text);
                return;
            }
            case NodeType.Bullet:
            {
                if (node.Marker is null || !BulletLevels.TryGetValue(node.Marker, out var level))
                {
                    // 마커 없는 본문 — annotation 폰트로 emit (직전 단락 폰트 잔류 방지).
                    EmitUnmarkeredProse(hwp, spec, node.Text);
                    return;
                }
                new BulletRenderer(hwp, spec).Render(level, node.Text, node.Summary);
                return;
            }
            case NodeType.Annotation:
            {
                var marker = node.Marker ?? "*";
                new AnnotationRenderer(hwp, spec).Render(marker, node.Text);
                return;
            }
            case NodeType.Conclusion:
                new ConclusionRenderer(hwp, spec).Render(node.Text);
                return;
            case NodeType.Callout:
            {
                // ★ child 의 .Text 만 뽑으면 파서가 분리한 Marker (○ * 등) 와 Summary
                //   ((20.9.) 등) 가 사라지는 사고. 원문 라인 형태로 재조립.
                //   예: Bullet(Marker="○", Summary="'20.9.", Text="구글이...")
                //        → "○ ('20.9.) 구글이..." 로 복원해 callout renderer 에 plain
                //          text 로 전달 (renderer 내부에서 __X__ bold 토큰은 InsertText 가 처리).
                var lines = node.Children
                    .Where(c => !string.IsNullOrEmpty(c.Text) || !string.IsNullOrEmpty(c.Marker))
                    .Select(ReconstructCalloutLine)
                    .ToList();
                if (node.CalloutKind == "note")
                    new NoteCalloutRenderer(hwp, spec).Render(lines);
                else
                    new AttachmentRenderer(hwp, spec).Render(node.CalloutNumber, lines);
                return;
            }
            case NodeType.Table:
                new TableRenderer(hwp, spec).Render(
                    node.Headers,
                    node.Rows.Cast<IReadOnlyList<string>>().ToList(),
                    node.Aligns.Count > 0 ? node.Aligns : null);
                return;
            case NodeType.Blank:
                return;  // dispatcher 가 직접 처리 — 도달 불가
        }

        // 알 수 없는 타입 — 마커 없는 본문 처리
        if (!string.IsNullOrEmpty(node.Text))
            EmitUnmarkeredProse(hwp, spec, node.Text);
    }

    /// <summary>
    /// Callout (참고/붙임) 박스 안에 child 를 plain 한 줄 텍스트로 복원.
    /// 파서가 Bullet 패턴으로 분리한 Marker (○ □ - · * ※ 등) + Summary (괄호 안) +
    /// Text 를 원문 그대로 합쳐 callout renderer 의 InsertText 한 번에 전달.
    /// (callout 내부는 글머리 layout 안 입히고 plain 본문으로 보여주는 정책.)
    /// </summary>
    private static string ReconstructCalloutLine(Node c)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(c.Marker)) sb.Append(c.Marker).Append(' ');
        if (!string.IsNullOrEmpty(c.Summary)) sb.Append('(').Append(c.Summary).Append(") ");
        sb.Append(c.Text);
        return sb.ToString();
    }

    /// <summary>
    /// 마커 없는 줄글 단락 — annotation spec(SSOT) 으로 emit.
    /// 직전 단락의 글머리 폰트·들여쓰기가 새는 사고를 막기 위해 명시적 reset.
    /// </summary>
    private static void EmitUnmarkeredProse(dynamic hwp, ReportSpec spec, string text)
    {
        var a = spec.Annotation;
        SetFont(hwp, a.Font, a.SizePt, bold: false);
        SetLineSpacing(hwp, a.LineSpacing);
        SetIndent(hwp, 0.0);
        AlignPara(hwp, Align.Justify);
        InsertText(hwp, text);
        BreakPara(hwp);
    }

    // ────────────────────────────────────────────────────────────────────
    // 저장
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// SaveAs .hwpx — Windows 경로 형식, format="HWPX" 우선 시도.
    /// public — Caller 가 변환 후 지연-저장 시 직접 호출 (outPath=null 로 변환 후).
    /// </summary>
    public static void SaveAsHwpx(dynamic hwp, string outPath)
    {
        outPath = outPath.Replace('/', '\\');
        try
        {
            hwp.SaveAs(outPath, "HWPX", "");
        }
        catch
        {
            // 오버로드 매칭 실패 시 1-arg fallback
            hwp.SaveAs(outPath);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // 선택 영역 변환 — 한/글 활성 문서의 selection 텍스트 → md 파싱 → 그 자리 변환
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 한/글 활성 문서의 선택 영역을 plain text 로 추출 → md 파싱 →
    /// 선택 영역을 변환 결과로 대체.
    ///
    /// Python convert_selection_to_hwpx 1:1 포팅. 동작:
    ///   1. selection_range() 검사 (단순 캐럿이면 NoSelectionException)
    ///   2. GetTextFile("UNICODE", "saveblock") 으로 선택 텍스트 추출 (서식 무시)
    ///   3. Run("Delete") 로 영역 제거 — caret 이 그 자리에 남음
    ///   4. parse_markdown → GenerateHwpxViaCom(mode=Cursor) — 그 위치에 emit
    ///
    /// tool2 `마크다운()` (한컴라이브러리_decompiled.py L12102+) 와 동일 패턴.
    /// </summary>
    public static int ConvertSelectionToHwpx(dynamic hwp, ReportSpec? spec = null, LogFn? log = null)
    {
        spec ??= ReportSpec.Report1;
        log ??= _ => { };

        // hwp 가 dynamic 이라 호출 site 가 dynamic dispatch → 반환된 Nullable 이 unwrap
        // 되어 .HasValue 못 잡힘 (RuntimeBinderException). object 로 cast 해 정적 호출.
        object hwpObj = hwp;
        var sel = Linter.Range.SelectionRange(hwpObj);
        log($"[md-convert] selection_range = {(sel.HasValue ? sel.Value.ToString() : "null")}");

        // 1) 선택 블록 텍스트 추출 — InitScan/GetText (메모리 스캔).
        //    ★ GetTextFile("UNICODE","saveblock") 직접 호출 금지 — 내부 SaveBlockAction 이
        //      한컴 보안 정책("보안 정책상 사용할 수 없는 기능입니다.") 을 발동시킴.
        //      GetSelectionText 가 tool2 블록텍스트(InitScan/GetText) 로 우회 + 다중 문단 누적.
        //    한계: 개체 선택 상태 (표/이미지/도형) 에서는 동작 안 함.
        string raw;
        try
        {
            raw = GetSelectionText(hwp);
        }
        catch (Exception e)
        {
            log($"[md-convert] 선택 텍스트 추출 실패: {e.Message}");
            throw new NoSelectionException(
                "선택 영역 텍스트 추출 실패 — 한/글 selection 상태를 확인해 주세요.", e);
        }

        var text = raw.TrimEnd();
        log($"[md-convert] GetTextFile 결과 = {text.Length}자");

        if (text.Length == 0)
            throw new NoSelectionException(
                "선택 영역이 인식되지 않거나 텍스트가 비어있습니다.\n" +
                "• 한/글에서 변환할 본문을 마우스 드래그로 영역 지정 후 단축키를 눌러주세요.\n" +
                "• 단순 캐럿 위치만으로는 동작하지 않습니다.\n" +
                "• 표·이미지 등 개체 선택 상태에서도 동작하지 않습니다.");

        if (sel is null)
            log("[md-convert] ⚠ GetSelectedPos 거짓음성 — GetTextFile 결과로 진행");

        // 2) 영역 삭제 — caret 이 그 자리에 남음
        log("[md-convert] 선택 영역 삭제 (Run='Delete')");
        hwp.Run("Delete");

        // 3) 파싱 + cursor 모드 변환
        var doc = Parser.Parse(text);
        log($"[md-convert] parse: 본문 노드 {doc.Nodes.Count}개");
        GenerateHwpxViaCom(
            hwp, doc,
            outPath: "",
            spec: spec,
            log: log,
            mode: HwpxWriteMode.Cursor);

        log($"[md-convert] ✔ 변환 완료 ({doc.Nodes.Count} 노드)");
        return doc.Nodes.Count;
    }
}

// ────────────────────────────────────────────────────────────────────────
// Python repr() 미니 헬퍼 — 로그 메시지의 'foo' / None 표기 일관성용
// ────────────────────────────────────────────────────────────────────────

internal static class ReprExtensions
{
    public static string Repr(this string? s) => s is null ? "None" : $"'{s}'";

    // Python f-string 의 `{x!r}` 를 흉내내는 확장 — 호출부에서 `x.Repr()` 또는
    // `x!r()` (위 GenerateHwpxViaCom 의 사용 패턴) 둘 다 동작.
}
