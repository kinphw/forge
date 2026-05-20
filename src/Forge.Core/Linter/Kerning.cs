// 어절 잘림 방지 자간조정 — tool1 `word_saver` 정확 포트.
// Python 원본 forge/linter/kerning.py 의 1:1 포팅.
//
// 알고리즘 (tool1 word_saver_text):
//   - 매 줄 끝의 마지막 어절을 검사
//   - front = MoveLineEnd → MoveSelWordBegin 으로 거꾸로 선택한 길이
//   - back  = MoveSelWordEnd 로 확장한 길이
//   - front >= back : SpacingDecrease (자간 -1) — 어절 끌어당김
//   - front <  back : SpacingIncrease (자간 +1) — 어절을 다음 줄로
//   - 줄당 최대 15회 시도, 실패 시 그 줄 변경 모두 Undo

using Forge.Core;

namespace Forge.Core.Linter;

public static class Kerning
{
    /// <summary>선택 영역의 자간 0 reset — tool2 `글자간격(0)` 패턴.</summary>
    public static void ResetKerningToZero(dynamic hwp)
    {
        ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object>
        {
            ["SpacingHangul"]   = 0,
            ["SpacingLatin"]    = 0,
            ["SpacingHanja"]    = 0,
            ["SpacingJapanese"] = 0,
            ["SpacingUser"]     = 0,
            ["SpacingSymbol"]   = 0,
            ["SpacingOther"]    = 0,
        });
    }

    /// <summary>tool1 word_saver_text 정확 포트 — 한 줄 자간 ±1 단계 ±15회.</summary>
    private static void AdjustOneLine(dynamic hwp, int maxAttempts = 15, LogFn? log = null)
    {
        log ??= _ => { };
        int count = 0;
        while (true)
        {
            hwp.Run("MoveLineEnd");
            hwp.Run("MoveSelWordBegin");
            if (count >= maxAttempts)
            {
                log($"    [line] {maxAttempts}회 초과 — Undo {count}번");
                for (int i = 0; i < count; i++) hwp.Run("Undo");
                break;
            }
            int front = IndentAlign.BlockLen(hwp);
            if (front == 0)
            {
                log("    [line] front=0 → 줄 끝 어절 없음, 종료");
                break;
            }
            hwp.Run("MoveSelWordEnd");
            int back = IndentAlign.BlockLen(hwp);
            if (front == 0 || back == 0)
            {
                log($"    [line] front={front} back={back} → 비정상, 종료");
                hwp.Run("Cancel");
                hwp.Run("Cancel");
                break;
            }
            hwp.Run("MoveWordBegin");
            hwp.Run("MoveLineEnd");
            hwp.Run("MoveSelLineBegin");
            if (front >= back)
            {
                log($"    [line#{count}] front={front} back={back} → SpacingDecrease");
                hwp.Run("CharShapeSpacingDecrease");
            }
            else
            {
                log($"    [line#{count}] front={front} back={back} → SpacingIncrease");
                hwp.Run("CharShapeSpacingIncrease");
            }
            count++;
            hwp.Run("Cancel");
        }
    }

    /// <summary>
    /// 현재 문단의 모든 줄에 자간조정. 끝나면 caret 은 다음 문단 시작 부근.
    /// 절차: 문단 전체 자간 0 reset → tool1 word_saver 알고리즘.
    /// </summary>
    public static void AdjustParagraph(object hwpObj, LogFn? log = null)
    {
        dynamic hwp = hwpObj;
        log ??= _ => { };

        // ★ Fast skip — 빈 문단이면 자간 처리 건너뜀 (dispatch overhead 회피)
        // string 명시 — BlockChar(dynamic hwp) 가 dynamic dispatch 되어 반환값이
        // dynamic 으로 wrap. 후속 IsNullOrWhiteSpace 가 정적 string 시그너처를
        // 못 잡으면 binder 가 깨질 위험. 명시 string 으로 정적 chain 복원.
        hwp.Run("MoveParaBegin");
        hwp.Run("MoveSelParaEnd");
        string fullText = IndentAlign.BlockChar(hwp);
        hwp.Run("Cancel");
        hwp.Run("MoveParaBegin");
        if (string.IsNullOrWhiteSpace(fullText))
        {
            log("  [empty para] kerning skip");
            hwp.Run("MoveNextParaBegin");
            return;
        }

        // 1. 자간 reset
        hwp.Run("MoveSelParaEnd");
        try
        {
            ResetKerningToZero(hwp);
            log("  [reset] 문단 자간 0 reset 완료");
        }
        catch (Exception e)
        {
            log($"  [reset] 자간 reset 실패: {e.Message}");
        }
        hwp.Run("Cancel");
        hwp.Run("MoveParaBegin");

        // 2. tool1 word_saver 알고리즘
        var start = Range.GetCaretPos(hwp);
        var startId = (start.List, start.Para);
        log($"  [para] start_id=({startId.List},{startId.Para})");

        const int maxLines = 1000;
        int iters = 0;
        while (iters < maxLines)
        {
            var prev = Range.GetCaretPos(hwp);
            if ((prev.List, prev.Para) != startId)
            {
                log($"  [para] 다음 문단 진입 ({prev}) — 종료");
                break;
            }
            log($"  [para] line iter#{iters} pos={prev}");
            AdjustOneLine(hwp, log: log);
            hwp.Run("MoveLineEnd");
            hwp.Run("MoveNextChar");
            var cur = Range.GetCaretPos(hwp);
            if (cur == prev)
            {
                log("  [para] 진행 멈춤 — 종료");
                break;
            }
            iters++;
        }
    }

    /// <summary>
    /// 본문 외 영역 (표 셀 등) 의 모든 list ID 순회 — 결론/참고 박스 안 자간조정.
    /// </summary>
    private static void AdjustObjects(dynamic hwp, LogFn log)
    {
        int area = 1;
        int objCount = 0;
        while (area < 100_000)
        {
            area++;
            try { Range.SetCaretPos(hwp, new CaretPos(area, 0, 0)); }
            catch { break; }
            var cur = Range.GetCaretPos(hwp);
            if (cur.List == 0) break;

            log($"  [object area={area}] cur={cur}");
            int innerIters = 0;
            while (innerIters < 1000)
            {
                var start = Range.GetCaretPos(hwp);
                AdjustParagraph(hwp, log);
                var newPos = Range.GetCaretPos(hwp);
                if (newPos.List != 0 && newPos.List >= area)
                    area = newPos.List;
                if (newPos == start) break;
                innerIters++;
            }
            objCount++;
        }
        log($"  [object] 처리된 area: {objCount}");
    }

    /// <summary>문서 전체 + 표 셀 등 객체 영역에 자간조정 (배치 모드 STAGE 2).</summary>
    public static void AdjustKerningToAvoidWordBreak(dynamic hwp, LogFn? log = null)
    {
        log ??= _ => { };

        log("[adjust_kerning] 본문 순회 시작");
        hwp.Run("MoveDocEnd");
        var endPos = Range.GetCaretPos(hwp);
        hwp.Run("MoveDocBegin");

        const int maxParas = 100_000;
        int iters = 0;
        while (iters < maxParas)
        {
            var prev = Range.GetCaretPos(hwp);
            if (prev == endPos) break;
            AdjustParagraph(hwp, log);
            var cur = Range.GetCaretPos(hwp);
            if (cur == prev)
            {
                log("  [본문 STOP] 진행 멈춤");
                break;
            }
            iters++;
        }
        log($"[adjust_kerning] 본문 처리 완료 ({iters} 문단)");

        log("[adjust_kerning] 객체 영역 순회 시작");
        AdjustObjects(hwp, log);
        log("[adjust_kerning] 객체 영역 처리 완료");
    }

    /// <summary>selection 이 있으면 범위, 없으면 현재 문단 1개 자간조정.</summary>
    public static void AdjustKerningCurrentParagraph(dynamic hwp, LogFn? log = null)
    {
        log ??= _ => { };
        log("[adjust_kerning_current_paragraph] 시작");
        ParaActionFn action = AdjustParagraph;
        Range.ApplyPerParagraph(hwp, action, log);
        log("[adjust_kerning_current_paragraph] 완료");
    }
}
