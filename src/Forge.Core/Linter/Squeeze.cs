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
// ★ KeyIndicator (out 파라미터, C# dynamic 부적합) 우회 — line 측정은 MoveLineDown
//   반복 + GetPosBySet 변화 감지로 대체.

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
        bool hasText = false;
        for (int trial = 0; trial < 10; trial++)
        {
            hwp.Run("MoveSelNextWord");
            var cRaw = IndentAlign.BlockChar(hwp);
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
        var c2Raw = IndentAlign.BlockChar(hwp);
        var c2 = c2Raw.Length > 0 ? c2Raw[..^1] : "";

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
            var curPos = Range.GetCaretPos(hwp);
            hwp.Run("MoveSelNextWord");
            var newPos = Range.GetCaretPos(hwp);
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
        var pos = Range.GetCaretPos(hwp);
        log($"  [body-start] pos={pos}");
        hwp.Run("Cancel");
        return pos;
    }

    /// <summary>
    /// 두 위치 사이의 줄 수 차이 (start → end). KeyIndicator 우회용.
    /// MoveLineDown 반복으로 측정. 동일 줄이면 0, end 가 더 아래면 양수.
    /// </summary>
    private static int LineDiff(dynamic hwp, CaretPos start, CaretPos end)
    {
        if (start == end) return 0;
        Range.SetCaretPos(hwp, start);
        int down = 0;
        const int max = 200;
        while (down < max)
        {
            var before = Range.GetCaretPos(hwp);
            // end 와 같거나 지나쳤으면 종료
            if (before.List == end.List && before.Para == end.Para && before.Pos >= end.Pos)
                return down;
            hwp.Run("MoveLineDown");
            var after = Range.GetCaretPos(hwp);
            if (after == before) break;  // 진행 멈춤
            down++;
            if (after.List == end.List && after.Para == end.Para && after.Pos >= end.Pos)
                return down;
        }
        return down;
    }

    /// <summary>문단 마지막 줄의 본문 텍스트 (마커 영역 제외).</summary>
    private static string LastLineText(dynamic hwp, CaretPos bodyStart)
    {
        Range.SetCaretPos(hwp, bodyStart);
        hwp.Run("MoveParaEnd");
        hwp.Run("MoveSelLineBegin");
        var text = IndentAlign.BlockChar(hwp).Trim();
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
        var bodyStart = FindBodyStartPos(hwp, log);
        if (bodyStart is null)
        {
            log("[fit_to_one_line] 빈 문단 — skip");
            return;
        }

        // 2. 문단 끝
        Range.SetCaretPos(hwp, bodyStart.Value);
        hwp.Run("MoveParaEnd");
        var paraEnd = Range.GetCaretPos(hwp);

        // 3. 줄 수 측정 (LineDiff 로 우회 — KeyIndicator 회피)
        int initialLines = LineDiff(hwp, bodyStart.Value, paraEnd) + 1;
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

            int newLines = LineDiff(hwp, bodyStart.Value, paraEnd) + 1;

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
