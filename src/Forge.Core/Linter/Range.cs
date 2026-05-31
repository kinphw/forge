// selection 범위 / 단일 캐럿 분기 헬퍼.
// Python 원본 forge/linter/_range.py 의 1:1 포팅.
//
// ★ Python `hwp.GetPos()` / `hwp.SetPos(...)` 는 out 파라미터 사용 → C# dynamic
//   바인더에 부적합. 한컴 공식 권고대로 ParameterSet 버전 사용:
//     - GetPosBySet(): ParameterSet 반환 → Item("List"/"Para"/"Pos") 추출
//     - SetPosBySet(pset): ParameterSet 으로 위치 설정
//   (HwpAutomation_2504.txt p.29 — "포인터를 사용할 수 없는 언어에서도 사용가능")

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
        log($"  [range] 다중 list ({startList} ~ {endList}) — 각 list 모든 문단 순회");
        int total = 0;
        int visited = 0;

        for (int listId = startList; listId <= endList; listId++)
        {
            try
            {
                SetCaretPos(hwp, new CaretPos(listId, 0, 0));
            }
            catch (Exception e)
            {
                log($"  [list#{listId}] SetPos 실패 ({e.Message}) — skip");
                continue;
            }
            var cur = GetCaretPos(hwp);
            if (cur.List != listId)
            {
                log($"  [list#{listId}] 도달 실패 (got {cur}) — skip");
                continue;
            }
            visited++;
            log($"  [list#{listId}] 진입, 문단 순회 시작");
            int innerIters = 0;
            int lastProcessedPara = -1;   // 같은 Para 재처리 방지 (ApplyWithinList 와 동일)
            while (innerIters < 1000)
            {
                var pPrev = GetCaretPos(hwp);
                if (pPrev.Para == lastProcessedPara)
                {
                    log($"  [list#{listId}] para {pPrev.Para} 이미 처리 — 다음 list");
                    break;
                }
                fn(hwp, log);
                lastProcessedPara = pPrev.Para;
                var pNew = GetCaretPos(hwp);
                if (pNew.List != listId)
                {
                    log($"  [list#{listId}] list 이탈 ({pNew}) — 다음 list");
                    break;
                }
                if (pNew == pPrev)
                {
                    log($"  [list#{listId}] 진행 멈춤 — 다음 list");
                    break;
                }
                innerIters++;
                total++;
            }
        }
        log($"  [range] 다중 list 처리 완료: 방문 {visited}/{endList - startList + 1} list, " +
            $"총 {total} 문단");
    }
}
