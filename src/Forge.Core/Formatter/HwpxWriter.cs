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

public sealed class NoSelectionException(string message) : Exception(message);

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

        // ─── STAGE 2 후처리 (new 모드만, W3 에서 활성화) ───
        if (mode == HwpxWriteMode.New)
        {
            if (applyIndentAlign || applyKerning)
                log("[STAGE 2] (보류) 들여쓰기 정렬·자간조정 — W3 에서 활성화 예정");
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
                var lines = node.Children.Where(c => !string.IsNullOrEmpty(c.Text)).Select(c => c.Text).ToList();
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
    // 선택 영역 변환 (W3 — linter 와 같이 포팅)
    // ────────────────────────────────────────────────────────────────────

    public static int ConvertSelectionToHwpx(dynamic hwp, ReportSpec? spec = null, LogFn? log = null) =>
        throw new NotImplementedException(
            "W3 에서 linter (selection_range) 와 함께 포팅 예정. " +
            "현재는 GenerateHwpxViaCom(mode=New) 로 새 hwpx 변환만 지원.");
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
