// selection 범위 / 단일 캐럿 분기 헬퍼.
// Python 원본 forge/linter/_range.py 의 1:1 포팅.
//
// ★ Python `hwp.GetPos()` / `hwp.SetPos(...)` 는 out 파라미터 사용 → C# dynamic
//   바인더에 부적합. 한컴 공식 권고대로 ParameterSet 버전 사용:
//     - GetPosBySet(): ParameterSet 반환 → Item("List"/"Para"/"Pos") 추출
//     - SetPosBySet(pset): ParameterSet 으로 위치 설정
//   (HwpAutomation_2504.txt p.29 — "포인터를 사용할 수 없는 언어에서도 사용가능")

using Forge.Interop.HwpObject;  // PIA — IHwpObject.InitScan/GetText (셀 블록 scan)

namespace Forge.Core.Linter;

public delegate void LogFn(string message);

/// <summary>문단 처리 콜백 — 한 문단 처리 후 caret 이 다음 문단 시작 부근으로 이동해야 함.</summary>
public delegate void ParaActionFn(object hwp, LogFn? log);

/// <summary>caret 위치 = (list, para, pos) 3-tuple.</summary>
public readonly record struct CaretPos(int List, int Para, int Pos)
{
    public override string ToString() => $"({List},{Para},{Pos})";
}

public static class Range
{
    private static readonly LogFn NoopLog = _ => { };

    /// <summary>
    /// 현재 selection 의 (start, end) 페어 반환. selection 없거나 start==end 면 null.
    /// 한컴 공식 (HwpAutomation_2504.txt p.34) 권고대로 GetSelectedPosBySet 사용 —
    /// 무인자 GetSelectedPos() 는 한/글 2010 등에서 "필수 매개변수입니다" 에러.
    /// </summary>
    public static (CaretPos Start, CaretPos End)? SelectionRange(dynamic hwp)
    {
        try
        {
            var sset = hwp.CreateSet("ListParaPos");
            var eset = hwp.CreateSet("ListParaPos");
            bool ok = (bool)hwp.GetSelectedPosBySet(sset, eset);
            if (!ok) return null;

            var start = new CaretPos((int)sset.Item("List"), (int)sset.Item("Para"), (int)sset.Item("Pos"));
            var end = new CaretPos((int)eset.Item("List"), (int)eset.Item("Para"), (int)eset.Item("Pos"));

            if (start == end) return null;
            return (start, end);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>caret 위치 가져오기 — GetPosBySet 사용 (out 파라미터 회피).</summary>
    public static CaretPos GetCaretPos(dynamic hwp)
    {
        try
        {
            var pset = hwp.GetPosBySet();
            return new CaretPos((int)pset.Item("List"), (int)pset.Item("Para"), (int)pset.Item("Pos"));
        }
        catch
        {
            return new CaretPos(-1, -1, -1);
        }
    }

    /// <summary>caret 위치 설정 — SetPosBySet 사용.</summary>
    public static void SetCaretPos(dynamic hwp, CaretPos pos)
    {
        var pset = hwp.CreateSet("ListParaPos");
        pset.SetItem("List", pos.List);
        pset.SetItem("Para", pos.Para);
        pset.SetItem("Pos", pos.Pos);
        hwp.SetPosBySet(pset);
    }

    /// <summary>
    /// selection (start~end) 범위 안에 앵커를 둔 인라인 개체(표·그리기 개체 등)의
    /// 앵커 문단 번호 목록 (오름차순, 중복 제거). HeadCtrl ~ Next linked-list 순회 +
    /// GetAnchorPos 범위 비교.
    ///
    /// ★ 용도: Ctrl+Shift+X 선택영역 md 변환이 표를 보존하도록 — 이 para 들을 경계로
    ///   selection 을 텍스트 구간으로 분할, 표 문단은 변환에서 제외(원본 유지).
    ///
    /// ★ PIA(IHwpObject) cast 가 안 되는 환경(한/글 2018 등 — GetText state 4 감지
    ///   불가)에서도 동작: HeadCtrl/Next/CtrlCh/GetAnchorPos 는 순수 dynamic dispatch.
    ///   또한 '글자처럼 취급'(inline) 표·'개체'(floating) 표 모두 조판부호(앵커)가
    ///   본문 list 에 있어 동일하게 잡힘 (한컴디벨로퍼 공식 답변 — 조판부호 위치 기준).
    ///
    /// 판별: CtrlCh == 11 = "그리기 개체 / 표" (HwpAutomation_2504 CtrlCh 표). 표(tbl)
    /// + 모든 ShapeObject($pic/$rec/$ell/…) 포괄. secd(구역)/cold(단) 등 구조 컨트롤은
    /// ch 가 달라 제외 — Delete 로 사라지지 않으므로 보존 대상 아님.
    /// </summary>
    public static List<int> CollectInlineObjectParas(dynamic hwp, CaretPos start, CaretPos end)
    {
        var paras = new SortedSet<int>();
        try
        {
            // dynamic COM linked-list 순회 — null 체크는 while 조건이 보장하나 dynamic
            // flow 분석이 못 따라가 CS8602 오탐. 억제.
#pragma warning disable CS8602
            dynamic ctrl = hwp.HeadCtrl;
            int guard = 0;
            while (ctrl != null && guard < 100_000)
            {
                int ch;
                try { ch = (int)ctrl.CtrlCh; } catch { ch = -1; }
                if (ch == 11)
                {
                    int? p = AnchorParaInRange(ctrl, start, end);
                    if (p.HasValue) paras.Add(p.Value);
                }
                ctrl = ctrl.Next;
                guard++;
            }
#pragma warning restore CS8602
        }
        catch { /* 순회 실패 시 빈 목록 — 기존 전체 변환으로 fallback */ }
        return new List<int>(paras);
    }

    /// <summary>selection 범위 안에 인라인 개체(표 등)가 있는지 (CollectInlineObjectParas wrapper).</summary>
    public static bool SelectionContainsInlineObject(dynamic hwp, CaretPos start, CaretPos end)
        => CollectInlineObjectParas(hwp, start, end).Count > 0;

    /// <summary>컨트롤 앵커(GetAnchorPos)가 selection [start, end] 범위 안이면 그 문단 번호, 아니면 null.</summary>
    private static int? AnchorParaInRange(dynamic ctrl, CaretPos start, CaretPos end)
    {
        try
        {
            dynamic ap = ctrl.GetAnchorPos(0);
            if (ap == null) return null;
            int list = (int)ap.Item("List");
            int para = (int)ap.Item("Para");
            int pos = (int)ap.Item("Pos");

            // 본문 list 범위 밖(표 셀 내부 list·머리말 등)은 selection 본문과 무관.
            if (list < start.List || list > end.List) return null;
            // 시작 list 에서 시작 위치보다 앞이면 제외.
            if (list == start.List && (para < start.Para || (para == start.Para && pos < start.Pos)))
                return null;
            // 끝 list 에서 끝 위치보다 뒤면 제외.
            if (list == end.List && (para > end.Para || (para == end.Para && pos > end.Pos)))
                return null;
            return para;
        }
        catch { return null; }
    }

    /// <summary>
    /// [startPara, endPara] 문단 구간을 블록 선택 (같은 list 가정). startPara 문단
    /// 처음부터 endPara 문단 끝까지. 검증된 MoveSel 액션(Kerning/IndentAlign 패턴) 기반:
    /// MoveParaBegin → MoveSelNextParaBegin×N → MoveSelParaEnd.
    /// </summary>
    public static void SelectParaRange(dynamic hwp, int list, int startPara, int endPara)
    {
        SetCaretPos(hwp, new CaretPos(list, startPara, 0));
        hwp.Run("MoveParaBegin");
        int hops = endPara - startPara;
        for (int i = 0; i < hops; i++)
            hwp.Run("MoveSelNextParaBegin");
        hwp.Run("MoveSelParaEnd");
    }

    /// <summary>
    /// selection 이 있으면 범위 내 모든 문단을 순회하며 fn 호출, 없으면 현재 문단 1개.
    ///
    /// fn 의 contract: 한 문단 처리 후 caret 이 다음 문단 시작 부근으로 이동
    /// (IndentAlign.ProcessParagraph / Kerning.AdjustParagraph 모두 충족).
    /// </summary>
    public static void ApplyPerParagraph(dynamic hwp, ParaActionFn fn, LogFn? log = null)
    {
        log ??= NoopLog;

        // ★ hwp 가 dynamic 이라 SelectionRange(hwp) 를 그대로 부르면 호출 site 가 dynamic
        //   dispatch → 반환된 Nullable<(CaretPos,CaretPos)> 이 런타임 바인더에서 underlying
        //   ValueTuple 로 unwrap 됨 → sel.Value 접근 시 RuntimeBinderException ('Value' 없음).
        //   선택 영역이 있을 때만 .Value 에 도달해 터짐 (단일 캐럿은 null 분기로 회피).
        //   object 로 cast 해 정적 호출 → 반환 타입 (..)? 보존. (HwpxWriter 와 동일 패턴.)
        object hwpObj = hwp;

        // ── 표 셀 블록 우선 처리 ──
        // 셀 블록(단일/다중 셀 선택)은 GetSelectedPos 가 신뢰 불가(start==end 로 접히거나
        // list 만 다르게 옴)라 SelectionRange 로는 단일 캐럿으로 오인 → 첫 문단만 처리되는
        // 버그. tool2 `셀정보`(디컴파일 line 1137) 권위 패턴(CellShape + scan)으로 셀
        // list 경계를 잡아 각 셀의 모든 문단을 순회한다.
        var cellRange = TryGetCellBlockRange(hwpObj, log);
        if (cellRange is not null)
        {
            var (lo, hi) = cellRange.Value;
            hwp.Run("Cancel");
            ApplyOverCells(hwp, fn, log, lo, hi);
            return;
        }

        var sel = SelectionRange(hwpObj);
        if (sel is null)
        {
            log("  [range] 단일 캐럿 — 현재 문단만");
            fn(hwp, log);
            return;
        }

        // tuple 분해 시 nullable record struct → explicit type
        CaretPos start = sel.Value.Start;
        CaretPos end = sel.Value.End;
        log($"  [range] selection: {start} → {end}");

        // selection 해제
        hwp.Run("Cancel");

        if (start.List == end.List)
            ApplyWithinList(hwp, fn, log, start, end);
        else
            ApplyAcrossLists(hwp, fn, log, start.List, end.List);
    }

    /// <summary>
    /// SelectionRange 대신 명시적 (start, end) 범위로 fn 적용 — pre-pass 가 selection 을
    /// 변형(빈줄 삽입 등) 한 뒤 호출용. 단일/다중 list 분기는 ApplyPerParagraph 와 동일.
    /// </summary>
    public static void ApplyPerParagraphInRange(
        dynamic hwp, ParaActionFn fn, CaretPos start, CaretPos end, LogFn? log = null)
    {
        log ??= NoopLog;
        log($"  [range] explicit: {start} → {end}");
        hwp.Run("Cancel");
        if (start.List == end.List)
            ApplyWithinList(hwp, fn, log, start, end);
        else
            ApplyAcrossLists(hwp, fn, log, start.List, end.List);
    }

    private static void ApplyWithinList(dynamic hwp, ParaActionFn fn, LogFn log, CaretPos start, CaretPos end)
    {
        SetCaretPos(hwp, start);
        const int maxIter = 1000;
        int iters = 0;
        int processed = 0;
        int lastProcessedPara = -1;   // 같은 Para 재처리 방지

        while (iters < maxIter)
        {
            var prev = GetCaretPos(hwp);
            if (prev.List != end.List)
            {
                log($"  [range] list 변경 ({prev.List} ≠ {end.List}) — 종료");
                break;
            }
            if (prev.Para > end.Para)
            {
                log($"  [range] end 문단({end.Para}) 초과 — 종료");
                break;
            }
            // ★ 마지막 문단 처리 후 MoveNextParaBegin 이 같은 Para 내 (Pos 만 변경) 로 떨어지는
            //   경우, 종료 검사(prev.Para > end.Para / newPos == prev) 가 한 박자 늦어 같은
            //   문단이 한 번 더 fn 으로 들어가는 사고. lastProcessedPara 로 명시 차단.
            if (prev.Para == lastProcessedPara)
            {
                log($"  [range] para {prev.Para} 이미 처리 — 종료");
                break;
            }

            log($"  [range#{iters}] 문단 (list={prev.List}, para={prev.Para}) 처리");
            fn(hwp, log);
            lastProcessedPara = prev.Para;
            processed++;

            var newPos = GetCaretPos(hwp);
            if (newPos == prev)
            {
                log("  [range] 진행 멈춤 — 종료");
                break;
            }
            iters++;
        }
        log($"  [range] 처리된 문단: {processed} 개 (단일 list)");
    }

    private static void ApplyAcrossLists(dynamic hwp, ParaActionFn fn, LogFn log, int startList, int endList)
    {
        // 드래그 방향에 따라 start.List > end.List 로 뒤집혀 올 수 있어 정규화 (셀 역드래그 등).
        int loList = Math.Min(startList, endList);
        int hiList = Math.Max(startList, endList);
        log($"  [range] 다중 list ({loList} ~ {hiList}) — 각 list 모든 문단 순회");
        int total = 0;
        int visited = 0;
        for (int listId = loList; listId <= hiList; listId++)
        {
            int p = ProcessOneList(hwp, fn, log, listId);
            if (p > 0) visited++;
            total += p;
        }
        log($"  [range] 다중 list 처리 완료: 방문 {visited}/{hiList - loList + 1} list, " +
            $"총 {total} 문단");
    }

    /// <summary>
    /// 표 셀 블록: [loList, hiList] 각 list(셀) 의 모든 문단을 순회 처리.
    /// tool2 `블록리스트생성`(디컴파일 line 1179) 이 확인해 준 사실 — 표 셀 list-id 는
    /// 행 우선(row-major) 연속 정수. 선택 사각형의 경계 셀 list 사이를 훑으면 선택 셀을
    /// 모두 포함한다(사이에 낀 비선택 셀도 같은 표라 정렬해도 무해 — 멱등 연산).
    /// </summary>
    private static void ApplyOverCells(dynamic hwp, ParaActionFn fn, LogFn log, int loList, int hiList)
    {
        log($"  [cell] 셀 블록 순회: list {loList} ~ {hiList}");
        int cells = 0, total = 0;
        for (int listId = loList; listId <= hiList; listId++)
        {
            int p = ProcessOneList(hwp, fn, log, listId);
            if (p > 0) { cells++; total += p; }
        }
        log($"  [cell] 완료: {cells} 셀, 총 {total} 문단");
    }

    /// <summary>
    /// list(본문/셀) 하나의 모든 문단을 순회 처리. 처리한 문단 수 반환.
    /// ApplyAcrossLists / ApplyOverCells 공용. fn 은 처리 후 caret 이 다음 문단 시작 부근.
    /// </summary>
    private static int ProcessOneList(dynamic hwp, ParaActionFn fn, LogFn log, int listId)
    {
        try
        {
            SetCaretPos(hwp, new CaretPos(listId, 0, 0));
        }
        catch (Exception e)
        {
            log($"  [list#{listId}] SetPos 실패 ({e.Message}) — skip");
            return 0;
        }
        var cur = GetCaretPos(hwp);
        if (cur.List != listId)
        {
            // 병합 등으로 존재하지 않는 list-id — 해당 셀 건너뜀.
            log($"  [list#{listId}] 도달 실패 (got {cur}) — skip");
            return 0;
        }

        int processed = 0;
        int lastPara = -1;   // 같은 Para 재처리 방지
        int iters = 0;
        while (iters < 1000)
        {
            var prev = GetCaretPos(hwp);
            if (prev.List != listId) break;          // list 이탈
            if (prev.Para == lastPara) break;        // 마지막 문단 후 제자리
            fn(hwp, log);
            lastPara = prev.Para;
            processed++;
            var nw = GetCaretPos(hwp);
            if (nw == prev) break;                   // 진행 멈춤
            iters++;
        }
        log($"  [list#{listId}] 처리 문단 {processed}");
        return processed;
    }

    // ────────────────────────────────────────────────────────────────────
    // 표 셀 블록 감지 (tool2 `셀정보` 권위 패턴)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 현재 캐럿이 표 안이고 "셀 블록"(단일/다중 셀 선택)이 잡혀 있으면 그 셀들의
    /// list-id 경계 (Lo, Hi) 를 반환. 표 밖 / bare caret / 셀 안 순수 텍스트 selection
    /// 이면 null (→ 기존 경로 유지, 무변경).
    ///
    /// ★ 왜 GetSelectedPos 로 안 되나: 셀 블록은 GetSelectedPosBySet 가 start==end 로
    ///   접히거나(단일 셀) list 만 다르게 오는 등 신뢰 불가. tool2 `셀정보`(디컴파일
    ///   line 1137)도 셀 블록엔 GetSelectedPos 대신 CellShape + InitScan/MovePos(scanPos)
    ///   기반을 씀 — 그 권위 패턴을 따른다.
    ///
    /// 감지 규칙:
    ///   0. CellShape 접근 예외 → 표 밖 → null (모든 일반 본문 케이스 무변경).
    ///   1. GetSelectedPos 가 같은 list 안 순수 텍스트 범위(start≠end, 같은 List) →
    ///      기존 ApplyWithinList 가 옳게 처리 → null. (증상 1 보존)
    ///   2. GetSelectedPos start.List ≠ end.List → 다중 셀 블록 → (min,max).
    ///   3. 그 외(start==end / ok=false):
    ///        SelectionMode(&0x0F)==0 → 선택 없음(bare caret) → null.
    ///        아니면 InitScan/GetText 로 블록 확인 + MovePos(scanPos) 로 시작 셀 list 획득
    ///        → 시작 셀 list 와 현재 캐럿 list 의 (min,max). (증상 2·3 해결)
    /// </summary>
    public static (int Lo, int Hi)? TryGetCellBlockRange(object hwpObj, LogFn? log = null)
    {
        log ??= NoopLog;
        dynamic hwp = hwpObj;

        // 0. 표 안인가 — CellShape 는 표 밖에서 접근 시 예외 (HwpAutomation p.12).
        try { object _ = hwp.CellShape; }
        catch { return null; }

        // 현재 캐럿 list (scan 이 캐럿을 옮기므로 먼저 확보)
        var caret0 = GetCaretPos(hwpObj);

        // raw selection (start==end 여도 접지 않고 그대로)
        var sel = SelectionRawInternal(hwpObj);

        // 1. 한 셀 안 순수 텍스트 selection — 기존 within-list 경로 유지
        if (sel.Ok && sel.Start != sel.End && sel.Start.List == sel.End.List)
        {
            log($"  [cell] 셀 안 텍스트 selection ({sel.Start}→{sel.End}) — within-list 경로");
            return null;
        }

        // 2. 서로 다른 list — 다중 셀 블록
        if (sel.Ok && sel.Start.List != sel.End.List)
        {
            int lo = Math.Min(sel.Start.List, sel.End.List);
            int hi = Math.Max(sel.Start.List, sel.End.List);
            log($"  [cell] 다중 셀 블록 (list {lo}~{hi}) — GetSelectedPos 기반");
            return (lo, hi);
        }

        // 3. start==end / ok=false — 단일 셀 블록 or bare caret
        int selMode = -1;
        try { selMode = ((int)hwp.SelectionMode) & 0x0F; } catch { /* 미지원 → scan 으로 판정 */ }
        if (selMode == 0)
        {
            log("  [cell] SelectionMode=0 (선택 없음) — bare caret, 기존 경로");
            return null;
        }

        var scan = ScanBlockFirstList(hwpObj, log);
        if (!scan.Active)
        {
            log("  [cell] scan 블록 미검출 — 기존 경로");
            return null;
        }
        int clo = Math.Min(scan.List, caret0.List);
        int chi = Math.Max(scan.List, caret0.List);
        log($"  [cell] 셀 블록 (scan-start list={scan.List}, caret list={caret0.List}) → {clo}~{chi}");
        return (clo, chi);
    }

    /// <summary>GetSelectedPosBySet 를 접지 않고 (Ok, Start, End) 그대로 반환.</summary>
    private static (bool Ok, CaretPos Start, CaretPos End) SelectionRawInternal(object hwpObj)
    {
        dynamic hwp = hwpObj;
        try
        {
            var sset = hwp.CreateSet("ListParaPos");
            var eset = hwp.CreateSet("ListParaPos");
            bool ok = (bool)hwp.GetSelectedPosBySet(sset, eset);
            if (!ok) return (false, default, default);
            var s = new CaretPos((int)sset.Item("List"), (int)sset.Item("Para"), (int)sset.Item("Pos"));
            var e = new CaretPos((int)eset.Item("List"), (int)eset.Item("Para"), (int)eset.Item("Pos"));
            return (true, s, e);
        }
        catch
        {
            return (false, default, default);
        }
    }

    /// <summary>
    /// 현재 블록을 InitScan(scanWithinSelection) + GetText 로 스캔하고, 텍스트가 있으면
    /// MovePos(moveScanPos=201) 로 캐럿을 블록 시작으로 옮긴 뒤 그 list-id 반환.
    /// tool2 `셀정보` 의 `InitScan(1,255); GetText(); MovePos(201)` 시퀀스와 동치.
    ///
    /// GetText 반환 2(일반 텍스트)/3(다음 문단) = 블록에 내용 있음(Active). 0/1/기타 =
    /// 블록 없음 → Active=false. KeyIndicator(out 파라미터, C# dynamic 부적합)는 쓰지 않음.
    /// </summary>
    private static (int List, bool Active) ScanBlockFirstList(object hwpObj, LogFn log)
    {
        dynamic hwp = hwpObj;
        try
        {
            if (hwpObj is IHwpObject typed)
            {
                typed.InitScan(null, 0xff, null, null, null, null);   // range=scanWithinSelection
                int state = typed.GetText(out string _);
                bool active = state == 2 || state == 3;
                if (active)
                {
                    try { hwp.MovePos(201, 0, 0); }   // moveScanPos → 블록 시작
                    catch { active = false; }
                }
                try { typed.ReleaseScan(); } catch { }
                if (!active) return (-1, false);
                var p = GetCaretPos(hwpObj);
                return (p.List, true);
            }
        }
        catch (Exception ex)
        {
            log($"  [cell] scan 예외: {ex.Message}");
            try { hwp.ReleaseScan(); } catch { }
        }
        return (-1, false);   // PIA cast 불가 환경 — 보수적으로 미검출
    }
}
