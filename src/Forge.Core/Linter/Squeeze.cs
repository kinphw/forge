// 어절 1개가 마지막 줄로 밀려난 케이스 — 자간 자동 조정으로 끌어올림.
// Python 원본 forge/linter/squeeze.py 의 1:1 포팅.
//
// 사용자 시나리오:
//   □ (단계적용) 원상회복 명령 우선, 특례는 보충적
//                                                활용
//                  ↑ "활용" 한 어절만 다음 줄로 밀림
//   → body 영역 자간 -1 씩 줄여서 한 줄에 합침
//
// 실시간 모드 (탭 ③) 전용 — 배치 모드 STAGE 2 에서는 호출 안 함.
//
// ★ 줄 측정 — LineDiff 만 사용 (MoveLineDown 반복 + GetPosBySet).
//   한컴 공식 KeyIndicator 시그너처: BOOL KeyIndicator(long*, long*, ..., 8개 out)
//   Remark 명시 "포인터를 사용할 수 없는 언어에서는 사용이 불가능". C# dynamic 으로
//   out 파라미터 수신 불가 → Python 의 KeyIndicator()[5] 방식 영구히 적용 불가.
//   Python pywin32 만 out 파라미터를 자동 tuple wrap 해서 가능했던 것.

namespace Forge.Core.Linter;

public static class Squeeze
{
    /// <summary>
    /// 현재 caret 이 위치한 문단의 본문 시작 위치 찾기 (마커 뒤 첫 body 워드).
    /// 빈 문단이면 null.
    /// </summary>
    private static CaretPos? FindBodyStartPos(dynamic hwp, LogFn log)
    {
        hwp.Run("MoveParaBegin");

        // 1. 첫 텍스트 워드 — 빈 문단 판정
        // ★ string 명시 — hwp 가 dynamic 이라 BlockChar 호출이 dynamic dispatch 되어
        //   반환값도 dynamic. var 로 받으면 후속 .Length / [..^1] indexer 가 dynamic
        //   dispatch 되며 RuntimeBinderException (string.this[int] vs Range slice).
        bool hasText = false;
        for (int trial = 0; trial < 10; trial++)
        {
            hwp.Run("MoveSelNextWord");
            string cRaw = IndentAlign.BlockChar(hwp);
            var stripped = cRaw.Trim();
            log($"  [skip-search#{trial}] raw={cRaw} stripped={stripped}");
            if (stripped.Length == 0)
            {
                hwp.Run("Cancel");
                continue;
            }
            hasText = true;
            log($"  [first-text] word={stripped} is_marker={IndentAlign.IsSkipMarker(stripped)}");
            hwp.Run("Cancel");
            break;
        }
        if (!hasText)
        {
            log("  [body-start] 빈 문단 — skip");
            return null;
        }

        // 2. 본문 워드 발견까지 점프
        hwp.Run("MoveParaBegin");
        hwp.Run("MoveSelNextWord");
        string c2Raw = IndentAlign.BlockChar(hwp);
        string c2 = c2Raw.Length > 0 ? c2Raw[..^1] : "";

        int iters = 0;
        bool found = false;
        CaretPos? lastPos = null;
        while (iters < 30)
        {
            if (IndentAlign.IsBodyWord(c2))
            {
                found = true;
                break;
            }
            hwp.Run("Cancel");
            // 정적 타입 명시 — hwp dynamic dispatch 결과의 dynamic 전염 차단
            CaretPos curPos = Range.GetCaretPos(hwp);
            hwp.Run("MoveSelNextWord");
            CaretPos newPos = Range.GetCaretPos(hwp);
            if (curPos == newPos && lastPos == curPos)
                break;
            lastPos = curPos;
            c2Raw = IndentAlign.BlockChar(hwp);
            c2 = c2Raw.Length > 0 ? c2Raw[..^1] : "";
            iters++;
        }

        if (!found)
        {
            log("  [body-start] 본문 워드 못 찾음");
            hwp.Run("Cancel");
            return null;
        }

        hwp.Run("MoveWordBegin");
        CaretPos pos = Range.GetCaretPos(hwp);
        log($"  [body-start] pos={pos}");
        hwp.Run("Cancel");
        return pos;
    }

    /// <summary>
    /// start ~ end 줄 수 (양 끝 포함). MoveLineDown 반복 + GetPosBySet 비교.
    /// 한컴 KeyIndicator 는 out 파라미터라 C# dynamic 에서 사용 불가.
    /// </summary>
    private static int MeasureLineSpan(dynamic hwp, CaretPos start, CaretPos end, LogFn log)
    {
        if (start == end) { log("  [line] start==end → 1 줄"); return 1; }
        Range.SetCaretPos(hwp, start);
        int down = 0;
        const int max = 200;
        while (down < max)
        {
            CaretPos before = Range.GetCaretPos(hwp);
            // ★ 표 셀 등에서 MoveLineDown 이 셀 경계를 탈출하면 list 가 변함.
            //   PosReachedOrPassed 는 list 다르면 false 만 반환 → 종료 못 잡고 문서 끝까지
            //   스캔하는 치명 버그. list 이탈을 명시적으로 검사해 즉시 중단.
            if (before.List != start.List)
            {
                log($"  [line] list 이탈 ({before.List} ≠ {start.List}) — 측정 중단, {down} 줄로 추정");
                return Math.Max(1, down);
            }
            if (PosReachedOrPassed(before, end))
            {
                log($"  [line] {down + 1} 줄 (도달, before={before})");
                return down + 1;
            }
            hwp.Run("MoveLineDown");
            CaretPos after = Range.GetCaretPos(hwp);
            if (after == before)
            {
                log($"  [line] 진행 멈춤 — {down + 1} 줄로 추정");
                break;
            }
            down++;
            if (PosReachedOrPassed(after, end))
            {
                log($"  [line] {down + 1} 줄 (도달, after={after})");
                return down + 1;
            }
        }
        return down + 1;
    }

    private static bool PosReachedOrPassed(CaretPos cur, CaretPos end)
    {
        if (cur.List != end.List) return false;
        if (cur.Para > end.Para) return true;
        if (cur.Para < end.Para) return false;
        return cur.Pos >= end.Pos;
    }

    /// <summary>문단 마지막 줄의 본문 텍스트 (마커 영역 제외).</summary>
    private static string LastLineText(dynamic hwp, CaretPos bodyStart)
    {
        Range.SetCaretPos(hwp, bodyStart);
        hwp.Run("MoveParaEnd");
        hwp.Run("MoveSelLineBegin");
        string raw = IndentAlign.BlockChar(hwp);
        var text = raw.Trim();
        hwp.Run("Cancel");
        return text;
    }

    /// <summary>
    /// 현재 caret 문단에서 마지막 줄에 어절 1 개만 있으면 자간 -1 반복으로 끌어올림.
    /// 어절 2 개 이상이면 skip.
    ///
    /// 실시간 모드 (탭 ③) 전용. 배치 모드 후처리에서 호출 안 함.
    /// </summary>
    public static void FitCurrentParagraphToOneLine(dynamic hwp, int maxIters = 30, LogFn? log = null)
    {
        log ??= _ => { };
        log("[fit_to_one_line] 시작");

        // 1. 본문 시작 위치
        // ★ 정적 타입 명시 — hwp 가 dynamic 이라 FindBodyStartPos / GetCaretPos
        //   호출이 dynamic dispatch 되어 반환값이 dynamic 으로 wrap.
        //   var 로 받으면 bodyStart.Value (nullable unwrap) 가 dynamic binder 에
        //   걸려 "CaretPos does not contain Value" RuntimeBinderException 발생.
        CaretPos? bodyStart = FindBodyStartPos(hwp, log);
        if (bodyStart is null)
        {
            log("[fit_to_one_line] 빈 문단 — skip");
            return;
        }

        // 2. 문단 끝
        Range.SetCaretPos(hwp, bodyStart.Value);
        hwp.Run("MoveParaEnd");
        CaretPos paraEnd = Range.GetCaretPos(hwp);

        // ★ MoveParaEnd 가 셀/list 를 탈출하는 경우 (HWP 버전·상황에 따른 quirk) — abort.
        //   계속하면 후속 측정/SpacingDecrease 가 잘못된 위치에 적용됨.
        if (paraEnd.List != bodyStart.Value.List)
        {
            log($"[fit_to_one_line] MoveParaEnd 가 list 탈출 ({paraEnd.List} ≠ {bodyStart.Value.List}) — skip");
            return;
        }

        // 3. 줄 수 측정 — KeyIndicator 우선 (Python 동등), 실패 시 LineDiff fallback
        int initialLines = MeasureLineSpan(hwp, bodyStart.Value, paraEnd, log);
        log($"  body_start={bodyStart.Value} para_end={paraEnd} → {initialLines} 줄");

        if (initialLines <= 1)
        {
            log("[fit_to_one_line] 이미 한 줄 — skip");
            return;
        }

        // 4. 마지막 줄 어절 수 검사
        var lastText = LastLineText(hwp, bodyStart.Value);
        var wordCount = string.IsNullOrEmpty(lastText)
            ? 0
            : lastText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        log($"  마지막 줄 텍스트={lastText} 어절수={wordCount}");

        if (wordCount != 1)
        {
            log($"[fit_to_one_line] 마지막 줄 어절 {wordCount} 개 (1 개 초과 또는 0) — skip");
            return;
        }

        // 5. SpacingDecrease 반복
        int target = initialLines - 1;
        int cur = initialLines;
        int noChangeCount = 0;
        const int noChangeLimit = 8;
        log($"  target={target} 줄, max_iters={maxIters}, no-change limit={noChangeLimit}");

        for (int i = 0; i < maxIters; i++)
        {
            Range.SetCaretPos(hwp, bodyStart.Value);
            hwp.Run("MoveSelParaEnd");
            hwp.Run("CharShapeSpacingDecrease");
            hwp.Run("Cancel");

            int newLines = MeasureLineSpan(hwp, bodyStart.Value, paraEnd, log);

            if (newLines <= target)
            {
                log($"  iter#{i + 1}: lines={newLines} ✔ 도달");
                log($"[fit_to_one_line] ✔ {newLines} 줄 도달 ({i + 1} 회 SpacingDecrease)");
                return;
            }

            if (newLines >= cur)
            {
                noChangeCount++;
                log($"  iter#{i + 1}: lines={newLines} (no-change {noChangeCount}/{noChangeLimit})");
                if (noChangeCount >= noChangeLimit)
                {
                    log($"[fit_to_one_line] ⚠ 연속 {noChangeLimit} 회 변화 없음 — 자간 한계, " +
                        $"현재 {cur} 줄 ({i + 1} 회 SpacingDecrease 적용 후)");
                    return;
                }
            }
            else
            {
                log($"  iter#{i + 1}: lines={newLines} (감소 {cur}→{newLines})");
                cur = newLines;
                noChangeCount = 0;
            }
        }

        log($"[fit_to_one_line] ⚠ {maxIters} 회 시도, 최종 {cur} 줄 — 한계");
    }
}
