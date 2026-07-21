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
        bool applyKerning = true,
        Func<bool>? isCancelled = null)
    {
        spec ??= ReportSpec.Report1;
        log ??= _ => { };
        isCancelled ??= () => false;

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

        // ─── 본문 노드 dispatcher (+ 인라인 정렬) ───
        log($"[STAGE 1] 본문 {doc.Nodes.Count} 노드 dispatcher 시작");
        // ★ 정렬(들·자·들) 을 STAGE 1 안에서 노드별로 즉시 처리 = 단일 하향 패스
        //   (사용자 요청 2026-07-16: 쓰기 후 본문 전체 순회 + 표 셀 순회로 문서를 여러 번
        //    훑던 것을 "쓰면서 그 자리에서 정렬" 로 통합). new 모드 + 두 정렬 모두 켜졌을
        //   때만 인라인. 단일 플래그(들만/자만)·cursor 모드는 아래 STAGE 2 블록에서 처리.
        bool alignInline = mode == HwpxWriteMode.New && applyIndentAlign && applyKerning;
        Linter.LogFn linterLog = msg => log("  " + msg);   // HwpxWriter/Linter LogFn 이름 충돌 bridge
        // forceBodyEnd: New 모드만 true — 매 노드 후 MoveDocEnd 로 nesting 누적 회피.
        // Cursor 모드 (X 단축키, 문서 중간 변환) 는 false — 캐럿이 변환 위치에 머물러야 함.
        DispatchNodes(hwp, doc.Nodes, spec, log,
            initialPrevEmitted: metadataEmitted,
            // ★ New 모드 (append-only) 의 정공법 — 매 노드 후 MoveDocEnd + BreakPara.
            //   2026-06-02 cross-AI 컨설팅 결론 (Gemini + Claude):
            //   - Run("MoveLineDown") 은 시각적 한 줄 이동이라 1×1 TreatAsChar 표 + 긴
            //     wrap 콘텐츠 + EOD 조합에서 no-op 됨 (HWP COM 의 알려진 flaky 동작).
            //   - 한컴 공식 패턴 CloseEx + MoveLineDown 은 UI 키 녹화 기반이라 자동화
            //     변환기에선 신뢰할 수 없는 anti-pattern.
            //   - append-only 정공법 = MoveDocEnd (캐럿 본문 끝) + BreakPara (명시적 단락
            //     분리, TreatAsChar 표가 다음 표와 같은 단락에 들러붙는 사고 회피).
            //   - X 모드 (Cursor) 가 동작했던 건 페이지 여백/줄간격 inherit 으로 표
            //     시각 layout 이 우연히 MoveLineDown 이 잡히는 좌표에 떨어진 운빨.
            forceBodyEnd: mode == HwpxWriteMode.New,
            isCancelled: isCancelled,
            alignEachNode: alignInline,
            alignLog: linterLog);
        if (isCancelled())
        {
            log("[STAGE 1] ⚠ 사용자 강제 중지 — 저장 skip");
            return "";
        }

        // ─── STAGE 2 후처리 — 단일 플래그(들만/자만)만 ───
        //   통합(들·자·들)은 위 STAGE 1 에서 노드별로 인라인 처리됨(alignInline).
        //   들여쓰기·자간 중 하나만 켜는 경우는 실사용에서 드물어(GUI 는 Q on/off 로 둘 다
        //   같이 토글) 기존 doc-wide 방식 유지. cursor 모드는 skip.
        if (mode == HwpxWriteMode.New && !alignInline)
        {
            if (applyIndentAlign)
            {
                log("[STAGE 2] 들여쓰기 정렬만 — 본문 1 pass");
                try { Linter.IndentAlign.AlignLeftIndent(hwp, linterLog, includeObjects: false); }
                catch (Exception e) { log($"  ⚠ 들여쓰기 정렬 중단: {e.Message}"); }
            }
            else if (applyKerning)
            {
                log("[STAGE 2] 자간조정만 — 본문 1 pass");
                try { Linter.Kerning.AdjustKerningToAvoidWordBreak(hwp, linterLog, includeObjects: false); }
                catch (Exception e) { log($"  ⚠ 자간조정 중단: {e.Message}"); }
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
        bool initialPrevEmitted,
        bool forceBodyEnd = false,
        Func<bool>? isCancelled = null,
        bool alignEachNode = false,
        Linter.LogFn? alignLog = null)
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

        // ★ 각 노드 렌더링 후 MoveDocEnd 로 본문(list 0) 끝으로 강제 복귀.
        //   특정 렌더러의 박스 exit (ExitTableAndJustify) 가 누적 상태에서 셀 탈출에
        //   실패해도 다음 노드는 항상 깨끗한 위치에서 시작. nesting 누적 회피.
        //   ★ Cursor 모드 (X 단축키: 문서 중간 변환) 는 forceBodyEnd=false — 캐럿이
        //     변환 위치에 머물러야 함 (MoveDocEnd 하면 캐럿이 문서 끝으로 튐).
        void ForceBodyEnd()
        {
            if (!forceBodyEnd) return;
            try { hwp.HAction.Run("MoveDocEnd"); }
            catch { /* 무시 */ }
        }

        var cancelCheck = isCancelled ?? (() => false);
        Linter.LogFn alog = alignLog ?? (_ => { });

        // ★ 노드를 쓴 직후 그 자리에서 (들·자·들) 정렬 → 문서를 두 번(쓰기+정렬) 나눠
        //   훑지 않고 단일 하향 흐름으로 처리 (사용자 요청, 2026-07-16). 정렬은 문단
        //   모양·자간만 바꾸고 텍스트/표 구조는 안 건드려 append 흐름을 깨지 않는다.
        //   - 본문(list 0) 문단: nodeStart~docEnd 범위를 AlignBodyRange 로 정렬
        //   - 결론박스(=>) 셀: DispatchOne 이 돌려준 셀 list-id 를 ApplyOverLists 로 정렬
        //     (표는 가운데정렬 정책이라 셀 반환 안 함 → 무변경)
        bool lastWasEmit = initialPrevEmitted;
        foreach (var node in nodes)
        {
            if (cancelCheck())
            {
                log("  ⚠ 사용자 강제 중지 요청 — dispatcher 종료");
                return;
            }
            if (node.Type == NodeType.Blank)
            {
                if (lastWasEmit)
                {
                    EmitBlankPara();
                    lastWasEmit = false;
                }
                continue;
            }

            // 이 노드가 쓰기 시작하는 본문 위치 (정렬 범위 시작점).
            Linter.CaretPos? nodeStart = null;
            if (alignEachNode)
                try { nodeStart = Linter.Range.GetCaretPos(hwp); } catch { /* 실패 시 정렬 skip */ }

            if (lastWasEmit) EmitBlankPara();

            try
            {
                int cellList = DispatchOne(hwp, node, spec);

                if (alignEachNode)
                {
                    if (nodeStart is { } start)
                        try { AlignBodyRange(hwp, start, alog, cancelCheck); }
                        catch (Exception e) { log($"  ⚠ 본문 정렬 중단: {e.Message}"); }
                    if (cellList >= 0)
                    {
                        // hwp 가 dynamic → 메서드 그룹 직접 전달은 CS1976. 델리게이트 변수 경유.
                        Linter.ParaActionFn cellAction = CombinedParaAction;
                        try { Linter.Range.ApplyOverLists(hwp, cellAction, new[] { cellList }, alog, cancelCheck); }
                        catch (Exception e) { log($"  ⚠ 박스 셀 정렬 중단: {e.Message}"); }
                    }
                }

                ForceBodyEnd();
                lastWasEmit = true;
            }
            catch (Exception e)
            {
                log($"  ✘ 노드 렌더링 실패 ({node.Type} marker={node.Marker.Repr()}): {e.Message}");
                try { BreakPara(hwp); } catch { }
                ForceBodyEnd();
                lastWasEmit = true;
            }
        }
    }

    /// <returns>
    /// 정렬이 필요한 박스 셀의 list-id (없으면 -1). 결론박스(=>)처럼 셀 안 본문이 wrap
    /// 되는 노드만 값을 돌려주고, 호출부(DispatchNodes)가 그 셀을 방금 쓴 자리에서 즉시
    /// 정렬한다. 나머지(본문 글머리 등 list 0 문단)는 호출부가 본문 범위 정렬로 처리.
    /// </returns>
    private static int DispatchOne(dynamic hwp, Node node, ReportSpec spec)
    {
        switch (node.Type)
        {
            case NodeType.Section:
            {
                int num = 0;
                if (node.Marker is not null && int.TryParse(node.Marker.TrimEnd('.'), out var parsed))
                    num = parsed;
                new SectionRenderer(hwp, spec).Render(num, node.Text);
                return -1;
            }
            case NodeType.Subsection:
            {
                var marker = node.Marker?.TrimEnd('.') ?? "";
                new SubsectionRenderer(hwp, spec).Render(marker, node.Text);
                return -1;
            }
            case NodeType.Bullet:
            {
                if (node.Marker is null || !BulletLevels.TryGetValue(node.Marker, out var level))
                {
                    // 마커 없는 본문 — annotation 폰트로 emit (직전 단락 폰트 잔류 방지).
                    EmitUnmarkeredProse(hwp, spec, node.Text);
                    return -1;
                }
                new BulletRenderer(hwp, spec).Render(level, node.Text, node.Summary);
                return -1;
            }
            case NodeType.Annotation:
            {
                var marker = node.Marker ?? "*";
                new AnnotationRenderer(hwp, spec).Render(marker, node.Text);
                return -1;
            }
            case NodeType.Conclusion:
                // 결론박스(=>)는 셀 안 본문이 wrap 되므로 정렬 대상. 셀 list-id 반환.
                return new ConclusionRenderer(hwp, spec).Render(node.Text);
            case NodeType.Callout:
            {
                // ★ child 의 .Text 만 뽑으면 파서가 분리한 Marker (○ * 등) 와 Summary
                //   ((20.9.) 등) 가 사라지는 사고. 원문 라인 형태로 재조립.
                //   예: Bullet(Marker="○", Summary="'20.9.", Text="구글이...")
                //        → "○ ('20.9.) 구글이..." 로 복원해 callout renderer 에 plain
                //          text 로 전달 (renderer 내부에서 **X** bold 토큰은 InsertText 가 처리).
                var lines = node.Children
                    .Where(c => !string.IsNullOrEmpty(c.Text) || !string.IsNullOrEmpty(c.Marker))
                    .Select(ReconstructCalloutLine)
                    .ToList();
                if (node.CalloutKind == "note")
                    new NoteCalloutRenderer(hwp, spec).Render(lines);
                else
                    new AttachmentRenderer(hwp, spec).Render(node.CalloutNumber, lines);
                return -1;
            }
            case NodeType.Table:
                // 표는 가운데정렬(TableStyle 정책)이라 정렬 대상 아님 — 셀 반환 안 함.
                new TableRenderer(hwp, spec).Render(
                    node.Headers,
                    node.Rows.Cast<IReadOnlyList<string>>().ToList(),
                    node.Aligns.Count > 0 ? node.Aligns : null);
                return -1;
            case NodeType.Blank:
                return -1;  // dispatcher 가 직접 처리 — 도달 불가
        }

        // 알 수 없는 타입 — 마커 없는 본문 처리
        if (!string.IsNullOrEmpty(node.Text))
            EmitUnmarkeredProse(hwp, spec, node.Text);
        return -1;
    }

    /// <summary>
    /// 방금 쓴 노드가 남긴 본문(list 0) 문단들(fromPos ~ 현재 문서끝)에 (들·자·들) 적용.
    /// STAGE 1 인라인 정렬용 — DispatchNodes 가 각 노드 직후 호출한다.
    ///
    /// fromPos 는 이 노드가 쓰기 시작한 위치(직전 노드 끝). 대부분 노드는 본문 1 문단이지만,
    /// 여러 문단이나 앞의 빈 spacer 도 범위에 들 수 있어 endPos 까지 문단 단위로 순회한다.
    /// 빈/한 줄 문단은 linter 자체가 skip. 캐럿은 호출 후 DispatchNodes 의 ForceBodyEnd 가
    /// 문서 끝으로 되돌린다.
    /// </summary>
    private static void AlignBodyRange(
        dynamic hwp, Linter.CaretPos fromPos, Linter.LogFn log, Func<bool>? shouldAbort)
    {
        hwp.HAction.Run("MoveDocEnd");
        var endPos = Linter.Range.GetCaretPos(hwp);
        Linter.Range.SetCaretPos(hwp, fromPos);

        int lastPara = -1, guard = 0;
        while (guard++ < 10_000)
        {
            if (shouldAbort?.Invoke() == true) break;
            var prev = Linter.Range.GetCaretPos(hwp);
            if (prev.List != 0) break;             // 본문 이탈 (있어선 안 됨)
            if (prev.Para > endPos.Para) break;    // 이 노드 범위 초과
            if (prev.Para == lastPara) break;      // 같은 문단 재처리 방지 (마지막 문단 후)

            CombinedParaAction(hwp, log);
            lastPara = prev.Para;

            var cur = Linter.Range.GetCaretPos(hwp);
            if (cur == prev) break;                // 진행 멈춤
        }
    }

    /// <summary>
    /// 한 문단에 (들·자·들) 3 단계 적용 — Q 단축키(RunAutoAlign) 의 combined 와 동일 로직.
    /// 본문 순회와 표 셀 순회가 공유하는 SSOT.
    ///
    /// 각 stage 사이 startPos 복원 — 자간조정의 wrap 위치 계산이 인덴트와 일치하게.
    /// ParaActionFn contract 충족: 마지막 ProcessParagraph 가 caret 을 다음 문단으로 이동.
    /// </summary>
    private static void CombinedParaAction(object hwpObj, Linter.LogFn? logArg)
    {
        dynamic h = hwpObj;
        Linter.LogFn log = logArg ?? (_ => { });

        // 문단 시작 pos 저장 — 각 stage 후 복원용.
        h.HAction.Run("MoveParaBegin");
        var startPos = Linter.Range.GetCaretPos(h);

        void Restore()
        {
            try { Linter.Range.SetCaretPos(h, startPos); }
            catch { h.HAction.Run("MoveParaBegin"); }
        }

        // 1) 1차 들여쓰기 — line wrap 기준 확정 (없으면 자간 효과 죽음)
        Linter.IndentAlign.ProcessParagraph(h, log);
        // 2) 자간조정 — 확정된 wrap 위에서 어절 잘림 보정
        Restore();
        Linter.Kerning.AdjustParagraph(h, log);
        // 3) 2차 들여쓰기 — 자간 drift 보정 (마지막 stage 가 캐럿을 다음 문단으로 이동)
        Restore();
        Linter.IndentAlign.ProcessParagraph(h, log);
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

        // ★ 선택 영역에 표·그리기 개체가 있으면 그 조판부호(앵커) 문단을 수집.
        //   '글자처럼 취급'(inline) 표·'개체'(floating) 표 모두 본문 list 에 조판부호가
        //   있어 동일하게 잡힘. (한컴디벨로퍼 공식 답변 + 사용자 실측 검증 2026-06-12)
        //   plain text 추출(GetTextFile)은 표 구조를 복원 못 하고, Run("Delete") 는 표를
        //   통째 지움 → 표를 변환 대상에서 제외(보존)해야 함.
        var tableParas = new List<int>();
        if (sel.HasValue)
        {
            try { tableParas = Linter.Range.CollectInlineObjectParas(hwp, sel.Value.Start, sel.Value.End); }
            catch (Exception e) { log($"[md-convert] 개체 순회 실패(무시): {e.Message}"); }
        }

        if (tableParas.Count > 0 && sel.HasValue)
        {
            // 표/개체 보존 — 표 문단을 경계로 텍스트 구간만 분할 변환.
            return ConvertSelectionPreservingObjects(
                hwp, spec, log, sel.Value.Start, sel.Value.End, tableParas);
        }

        // 표 없음 (또는 sel 거짓음성) — 선택 영역 전체를 한 번에 변환 (기존 경로).
        return ConvertSelectionWhole(hwp, spec, log, sel is not null);
    }

    /// <summary>
    /// 선택 영역 전체를 한 번에 변환 — 표/개체가 없는 경우. (기존 동작)
    /// </summary>
    private static int ConvertSelectionWhole(dynamic hwp, ReportSpec spec, LogFn log, bool selDetected)
    {
        // 선택 블록 텍스트 추출 — InitScan/GetText (메모리 스캔), 실패 시 GetTextFile fallback.
        //   ★ GetTextFile("UNICODE","saveblock") 직접 호출 금지 — 내부 SaveBlockAction 이
        //     한컴 보안 정책을 발동시킴. GetSelectionText 가 우회 + 다중 문단 누적.
        string raw;
        bool containsObject;
        try
        {
            // ★ (object) 캐스팅 — dynamic dispatch 시 out 파라미터 런타임 바인딩이 불안정.
            raw = GetSelectionText((object)hwp, out containsObject);
        }
        catch (Exception e)
        {
            log($"[md-convert] 선택 텍스트 추출 실패: {e.Message}");
            throw new NoSelectionException(
                "선택 영역 텍스트 추출 실패 — 한/글 selection 상태를 확인해 주세요.", e);
        }

        // sel 거짓음성(GetSelectedPos 실패)이라 표 para 수집을 못 했는데 PIA 경로로 개체가
        // 잡힌 경우 — 구간 분할이 불가하므로 표 손실을 막기 위해 거부.
        if (containsObject)
        {
            log("[md-convert] ⚠ 선택 영역에 표/개체 포함 (범위 미상) — 변환 거부 (원본 보존)");
            throw new NoSelectionException(
                "선택 영역에 표·이미지 등 개체가 포함되어 있어 변환할 수 없습니다.\n" +
                "• 표·이미지·도형을 제외하고 본문 텍스트만 선택한 뒤 다시 시도해 주세요.");
        }

        var text = raw.TrimEnd();
        log($"[md-convert] 추출 텍스트 = {text.Length}자");

        if (text.Length == 0)
            throw new NoSelectionException(
                "선택 영역이 인식되지 않거나 텍스트가 비어있습니다.\n" +
                "• 한/글에서 변환할 본문을 마우스 드래그로 영역 지정 후 단축키를 눌러주세요.\n" +
                "• 단순 캐럿 위치만으로는 동작하지 않습니다.\n" +
                "• 표·이미지 등 개체 선택 상태에서도 동작하지 않습니다.");

        if (!selDetected)
            log("[md-convert] ⚠ GetSelectedPos 거짓음성 — 추출 텍스트로 진행");

        log("[md-convert] 선택 영역 삭제 (Run='Delete')");
        hwp.Run("Delete");

        var doc = Parser.Parse(text);
        log($"[md-convert] parse: 본문 노드 {doc.Nodes.Count}개");
        GenerateHwpxViaCom(hwp, doc, outPath: "", spec: spec, log: log, mode: HwpxWriteMode.Cursor);

        log($"[md-convert] ✔ 변환 완료 ({doc.Nodes.Count} 노드)");
        return doc.Nodes.Count;
    }

    /// <summary>
    /// 표/개체를 보존하며 선택 영역을 변환 — 표 조판부호 문단을 경계로 selection 을
    /// 텍스트 구간으로 쪼개고, 표 문단은 건드리지 않은 채 각 구간만 변환.
    ///
    /// ★ 역순(아래→위) 처리: 한 구간 변환은 그보다 아래 문단 번호를 바꾸므로, 아래부터
    ///   처리하면 아직 처리 안 한 위쪽 구간·표의 절대 문단 번호가 보존됨.
    /// ★ 단일 list(start.List == end.List) 가정 — 일반적인 본문 선택. 표 셀에 걸친
    ///   다중 list 선택은 SelectParaRange 가 본문 list 만 다뤄 보수적으로 동작.
    /// </summary>
    private static int ConvertSelectionPreservingObjects(
        dynamic hwp, ReportSpec spec, LogFn log,
        Linter.CaretPos start, Linter.CaretPos end, List<int> tableParas)
    {
        log($"[md-convert] 표/개체 {tableParas.Count}개 보존 — 구간 분할 변환 " +
            $"(표 문단: {string.Join(",", tableParas)})");

        int list = start.List;

        // 표 문단을 경계로 텍스트 구간 [s, e] 분할 (표 문단 자체는 제외).
        var segments = new List<(int s, int e)>();
        int cur = start.Para;
        foreach (int t in tableParas)  // 오름차순
        {
            if (cur <= t - 1) segments.Add((cur, t - 1));
            cur = t + 1;
        }
        if (cur <= end.Para) segments.Add((cur, end.Para));

        log($"[md-convert] 텍스트 구간 {segments.Count}개: " +
            string.Join(" ", segments.ConvertAll(s => $"[{s.s}~{s.e}]")));

        int total = 0;
        // 역순 — 아래 구간부터 변환해 위쪽 문단 번호 보존.
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            var (s, e) = segments[i];
            try
            {
                Linter.Range.SelectParaRange(hwp, list, s, e);
            }
            catch (Exception ex)
            {
                log($"[md-convert] 구간 [{s}~{e}] 선택 실패: {ex.Message} — skip");
                continue;
            }

            string raw;
            try { raw = GetSelectionText((object)hwp, out _); }
            catch (Exception ex)
            {
                log($"[md-convert] 구간 [{s}~{e}] 추출 실패: {ex.Message} — skip");
                try { hwp.Run("Cancel"); } catch { }
                continue;
            }

            var text = raw.TrimEnd();
            if (text.Length == 0)
            {
                // 표 사이 빈 줄 구간 — 변환/삭제 없이 그대로 보존.
                log($"[md-convert] 구간 [{s}~{e}] 빈 텍스트 — 보존 (skip)");
                try { hwp.Run("Cancel"); } catch { }
                continue;
            }

            log($"[md-convert] 구간 [{s}~{e}] 변환 ({text.Length}자)");
            hwp.Run("Delete");
            var doc = Parser.Parse(text);
            GenerateHwpxViaCom(hwp, doc, outPath: "", spec: spec, log: log, mode: HwpxWriteMode.Cursor);
            total += doc.Nodes.Count;
        }

        log($"[md-convert] ✔ 표 보존 변환 완료 (총 {total} 노드, 표/개체 {tableParas.Count}개 유지)");
        return total;
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
