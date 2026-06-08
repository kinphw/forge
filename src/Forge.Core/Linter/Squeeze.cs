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

    /// <summary>자간값 읽기 실패 sentinel — floor 판정에서 제외.</summary>
    private const int SpacingUnknown = int.MinValue;

    /// <summary>
    /// 지정 위치 캐럿의 현재 한글 자간(%) 읽기. CharShape ParameterSet 의 SpacingHangul
    /// (PIT_I1, 범위 -50%~50% — hwp-api CharShape id:13). 읽기 실패 시 SpacingUnknown.
    ///
    /// FitCurrentParagraphToOneLine 의 가독성 floor 판정용 — paraEnd 직전 글자는 매 iter
    /// kernStart~paraEnd 자간 감소 영역에 항상 포함되므로 단조 감소 → 신뢰 가능한 probe.
    ///
    /// ★ collapsed 캐럿(at)에서 바로 GetDefault 하면 typing attribute 가 직전 값을 한 박자
    ///   늦게 반영(stale)하는 경우가 있어 — 마지막 글자 1개를 실제 선택해 그 글자의 문서
    ///   모델 값을 읽음. 단일 글자 선택이라 GetDefault 가 mixed-value sentinel 없이 정확값 반환.
    /// PIT_I1 이 부호 없는 byte 로 넘어오는 경우(예: -30 → 226) 대비, 유효범위(-50~50)
    /// 밖이면 256 보정해 부호 복원.
    /// </summary>
    private static int ReadSpacingHangul(dynamic hwp, CaretPos at, LogFn log)
    {
        try
        {
            Range.SetCaretPos(hwp, at);
            hwp.Run("MoveSelPrevChar");   // 마지막 글자 1개 선택 (collapsed 캐럿 stale 회피)
            var cs = hwp.HParameterSet.HCharShape;
            hwp.HAction.GetDefault("CharShape", cs.HSet);
            object? v = ((object)cs).GetType().InvokeMember(
                "SpacingHangul", System.Reflection.BindingFlags.GetProperty, null, cs, null);
            hwp.Run("Cancel");
            if (v is null) return SpacingUnknown;
            int n = Convert.ToInt32(v);
            if (n > 50) n -= 256;   // 부호 없는 byte wraparound 복원
            return n;
        }
        catch (Exception e)
        {
            log($"  [spacing-read] 실패 ({e.Message}) — fallback(줄수 plateau) 에 의존");
            try { hwp.Run("Cancel"); } catch { /* 선택 정리 best-effort */ }
            return SpacingUnknown;
        }
    }

    /// <summary>
    /// 현재 문단의 "마지막 직전 줄" 시작 위치 반환 — bodyStart ~ paraEnd 안에서.
    /// 호출 전 캐럿 위치는 무관 (내부에서 paraEnd 까지 이동 후 한 줄 위로).
    /// 만약 직전 줄 시작이 bodyStart 보다 앞이면 (2-줄 문단 + 마커 영역 침범 케이스),
    /// bodyStart 로 clamp.
    /// </summary>
    private static CaretPos ComputeSecondLastLineStart(dynamic hwp, CaretPos bodyStart, LogFn log)
    {
        Range.SetCaretPos(hwp, bodyStart);
        hwp.Run("MoveParaEnd");
        hwp.Run("MoveLineBegin");
        // CaretPos lastLineStart = Range.GetCaretPos(hwp);  // 디버깅용
        hwp.Run("MoveUp");
        hwp.Run("MoveLineBegin");
        CaretPos kernStart = Range.GetCaretPos(hwp);

        // bodyStart 보다 앞이면 (마커 영역 침범) bodyStart 로 clamp
        bool isBefore =
            kernStart.List != bodyStart.List
            || kernStart.Para < bodyStart.Para
            || (kernStart.Para == bodyStart.Para && kernStart.Pos < bodyStart.Pos);
        if (isBefore)
        {
            log($"  [kern-range] 직전 줄 시작 {kernStart} < bodyStart {bodyStart} — bodyStart 로 clamp");
            return bodyStart;
        }
        log($"  [kern-range] 직전 줄 시작 = {kernStart}");
        return kernStart;
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
    /// <summary>
    /// 과압축 방지 floor — 이 자간(%) 밑으로는 안 좁힘. 0~-50 사이.
    /// "끝까지 합칠 때까지" 의도를 살리되 글자 겹침·가독성 파괴(한/글 물리 한계 -50%)는 회피.
    /// ★ 시각 튜닝 knob — 더 공격적으로 합치려면 -50 쪽으로, 보수적이면 0 쪽으로.
    /// </summary>
    private const int MinSpacingPct = -30;

    public static void FitCurrentParagraphToOneLine(dynamic hwp, int maxIters = 60, LogFn? log = null)
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

        // 4. 마지막 줄 텍스트 로그용 (조건 검사는 안 함).
        //    2026-06-02 사용자 요청: 1-어절 조건 제거. 어절이 몇 개든 마지막 2 줄 자간
        //    좁혀 1 줄 줄임 시도. 자간 한계 도달하면 noChangeLimit 로 자연 종료.
        var lastText = LastLineText(hwp, bodyStart.Value);
        log($"  마지막 줄 텍스트={lastText}");

        // 5. SpacingDecrease 반복 — 마지막 직전 줄부터 paraEnd 까지만 자간 적용.
        //    (예전: bodyStart → paraEnd 전체 → 3줄+ 문단의 앞줄들이 불필요하게 줄어 망가짐.)
        //    매 iter 마다 자간 감소로 wrap 위치가 바뀌므로 "마지막 직전 줄 시작 위치" 재계산.
        //    2-줄 문단의 경우 직전 줄 시작이 bodyStart 보다 앞쪽(마커 영역)으로 가버리니
        //    bodyStart 로 clamp — 마커 자간은 보존.
        //
        // ★ 종료 조건 (2026-06-04 — "줄 합쳐질 때까지 시도" 요청):
        //    예전 noChangeLimit=8 은 "연속 8회 줄수 무변화" 추정으로, 마지막 줄 어절이
        //    길어 자간을 9회+ 좁혀야 당겨지는 경우 미리 포기하는 문제가 있었음.
        //    ★ 성공 판정은 그대로 "줄 수 측정"(newLines<=target). 자간값은 floor 판정에만 사용:
        //     (a) 합쳐짐(newLines<=target) → 성공
        //     (b) 가독성 floor(MinSpacingPct=-30%) 도달 → 과압축 방지 종료
        //     (c) [fallback] 자간 read 실패 시에만 의미 있는 줄수 plateau 카운터 — read 정상
        //         시엔 (b)(-30%, ~30회)가 항상 먼저라 발동 안 함. 무한루프 방지 backstop.
        //     (d) [hard backstop] maxIters — UI 스레드 실행이라 프리징 절대 회피.
        //    ※ "자간이 더 안 변하면 물리 바닥" 직접 종료(예전 c)는 제거. floor(-30%)가 물리
        //       바닥(-50%)보다 위라 정상 시 절대 안 쓰이고, probe stale 시 -11% 등에서
        //       오발화해 조기 종료시키기만 했음 (재호출하면 이어서 진행돼 합쳐지는 증상).
        int target = initialLines - 1;
        int cur = initialLines;
        int noChangeCount = 0;
        const int noChangeLimit = 35;   // fallback only — 정상 read 시 (b)(-30%)가 먼저 발동
        int spacing = ReadSpacingHangul(hwp, paraEnd, log);   // 시작 자간 probe
        log($"  target={target} 줄, max_iters={maxIters}, floor={MinSpacingPct}%, 시작 자간={spacing}%");

        for (int i = 0; i < maxIters; i++)
        {
            // (b) 가독성 floor — 이미 바닥이면 더 안 좁히고 종료
            if (spacing != SpacingUnknown && spacing <= MinSpacingPct)
            {
                log($"[fit_to_one_line] ⊘ 자간 floor({MinSpacingPct}%) 도달 — 과압축 방지 종료, " +
                    $"현재 {cur} 줄 ({i} 회 적용 후)");
                return;
            }

            CaretPos kernStart = ComputeSecondLastLineStart(hwp, bodyStart.Value, log);
            Range.SetCaretPos(hwp, kernStart);
            hwp.Run("MoveSelParaEnd");
            hwp.Run("CharShapeSpacingDecrease");
            hwp.Run("Cancel");

            int newLines = MeasureLineSpan(hwp, bodyStart.Value, paraEnd, log);

            // (a) 합쳐짐 → 성공
            if (newLines <= target)
            {
                log($"  iter#{i + 1}: lines={newLines} ✔ 도달");
                log($"[fit_to_one_line] ✔ {newLines} 줄 도달 ({i + 1} 회 SpacingDecrease)");
                return;
            }

            spacing = ReadSpacingHangul(hwp, paraEnd, log);   // 다음 iter (b) floor 판정용 갱신

            // (c) fallback: 줄수 plateau (자간 read 실패해 (b) 가 무력할 때만 의미)
            if (newLines >= cur)
            {
                noChangeCount++;
                log($"  iter#{i + 1}: lines={newLines} spacing={spacing}% " +
                    $"(no-change {noChangeCount}/{noChangeLimit})");
                if (noChangeCount >= noChangeLimit)
                {
                    log($"[fit_to_one_line] ⚠ 연속 {noChangeLimit} 회 줄수 무변화(자간 read 실패 추정) — " +
                        $"종료, 현재 {cur} 줄 ({i + 1} 회 적용 후)");
                    return;
                }
            }
            else
            {
                log($"  iter#{i + 1}: lines={newLines} spacing={spacing}% (감소 {cur}→{newLines})");
                cur = newLines;
                noChangeCount = 0;
            }
        }

        log($"[fit_to_one_line] ⚠ maxIters({maxIters}) 도달 — 종료, 최종 {cur} 줄");
    }
}
