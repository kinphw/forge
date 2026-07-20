// 상용구 확장 — 캐럿 바로 앞 준말(before)을 본말(after)로 치환.
// Ctrl+Shift+I (glossary_expand) 진입점.
//
// 동작 (두 모드):
//   A. 선택 영역이 있으면 — 캐럿 앞이 아니라 "선택된 글자" 를 읽어 매치.
//      선택 텍스트가 등록 준말과 정확히 일치하면 Delete → InsertText(after) 치환.
//   B. 선택이 없으면(캐럿만) — 캐럿 앞 글자(MoveSelPrevChar)로만 매치. Ctrl+Shift+I 는
//      먼저 오른쪽 방향키를 주입해 IME 조합을 확정 + 캐럿을 방금 친 글자 '뒤'로 보내므로
//      (RealtimeTab), 앞 글자만 읽으면 된다. 등록 상용구 before 길이 내림차순(긴 준말
//      우선 ">>" > ">"). 매치 없으면 캐럿 원위치 복원.
//      ★ 예전엔 "뒤 방향(MoveSelNextChar)" fallback 도 있었으나 (1) 방향키 확정으로
//        불필요해졌고 (2) 문서 끝에서 빈 선택에 BlockChar→GetTextFile("saveblock")를
//        호출해 한/글이 보안/블록 모달로 빠져 응답없음(hang)을 유발 → 제거.
//
// ★ 선택 텍스트 읽기는 IndentAlign.BlockChar (InitScan/GetText, 비파괴) 재사용 —
//   선택 상태 보존됨(IndentAlign 순회가 동일 패턴 사용). 삭제는 codebase 관례인
//   Run("Delete") (HwpxWriter 선택영역 삭제와 동일). 선택 유무는 Range.SelectionRange
//   (GetSelectedPosBySet, start≠end) 로 판정.

namespace Forge.Core.Linter;

public static class GlossaryExpand
{
    /// <summary>
    /// 캐럿 바로 앞 텍스트가 등록된 상용구 before 와 일치하면 after 로 치환.
    /// 반환: 치환했으면 true, 매치 없으면 false.
    /// </summary>
    public static bool ExpandAtCaret(dynamic hwp, IReadOnlyList<GlossaryEntry> entries, LogFn? log = null)
    {
        log ??= _ => { };
        if (entries is null || entries.Count == 0)
        {
            log("  [상용구] 등록된 항목 없음");
            return false;
        }

        object hwpObj = hwp;

        // ── 모드 A: 선택 영역이 있으면 선택된 글자로 매치 (캐럿 앞 아님) ──
        var sel = Range.SelectionRange(hwpObj);
        if (sel is not null)
        {
            string selText = IndentAlign.BlockChar(hwp);   // 선택 보존됨
            log($"  [상용구] 선택 영역='{selText}'");
            foreach (var e in entries)   // 선택은 정확 일치 1건만 — 순서 무관
            {
                if (selText == e.Before)
                {
                    hwp.Run("Delete");                     // 선택 삭제
                    ComHelpers.InsertText(hwp, e.After);   // 본말 삽입
                    log($"  [상용구] ✔ (선택) '{e.Before}' → '{e.After}'");
                    return true;
                }
            }
            log("  [상용구] 선택 텍스트가 등록 준말과 불일치 — no-op");
            return false;
        }

        // ── 모드 B: 선택 없음 — 캐럿 앞 글자로 매치 ──
        // CommitComposition(오른쪽 방향키)이 캐럿을 방금 친 글자 '뒤'로 보내므로 앞 글자만
        // 읽으면 된다.
        var origin = Range.GetCaretPos(hwpObj);
        if (origin.List < 0)
        {
            log("  [상용구] 캐럿 위치 확인 실패");
            return false;
        }

        if (TryMatchBefore(hwp, hwpObj, entries, origin, log)) return true;

        // 매치 없음 — 캐럿 원위치 복원 (선택 해제 포함)
        try { hwp.Run("Cancel"); } catch { }
        try { Range.SetCaretPos(hwpObj, origin); } catch { }
        log("  [상용구] 매치 없음 — 캐럿 앞 텍스트가 등록 준말과 불일치");
        return false;
    }

    /// <summary>
    /// origin 캐럿에서 앞으로(MoveSelPrevChar) 준말 길이만큼 선택해 등록 준말과 일치하면
    /// 치환. 긴 준말 우선.
    /// ★ 실제로 선택이 잡혔을 때만 BlockChar 를 호출한다 — 문서 시작 등에서 선택이 비면
    ///   GetTextFile("saveblock") 이 한/글 보안/블록 모달을 띄워 hang 될 수 있어, 빈 선택은
    ///   BlockChar 없이 skip.
    /// </summary>
    private static bool TryMatchBefore(
        dynamic hwp, object hwpObj, IReadOnlyList<GlossaryEntry> entries,
        CaretPos origin, LogFn log)
    {
        // ★ 속도: 준말 '길이'별로 한 번만 선택·읽기 (BlockChar 호출 최소화).
        //   기본 5종은 전부 길이 1 → 읽기 1회로 5종 모두 비교하고 끝.
        //   길이 내림차순 — 긴 준말 우선(">>" > ">").
        foreach (int n in entries.Select(e => e.Before.Length).Where(x => x > 0)
                                 .Distinct().OrderByDescending(x => x))
        {
            hwp.Run("Cancel");
            Range.SetCaretPos(hwpObj, origin);
            for (int i = 0; i < n; i++) hwp.Run("MoveSelPrevChar");

            // 실제 n 글자 선택 확인 — 빈/부분 선택이면 BlockChar 없이 skip (hang 회피).
            if (Range.SelectionRange(hwpObj) is null)
            {
                log($"  [상용구] 앞 len={n} 선택 없음 — skip");
                continue;
            }

            string got = IndentAlign.BlockChar(hwp);   // 이 길이당 딱 1회
            log($"  [상용구] 앞 len={n} 선택='{got}'");

            foreach (var e in entries)
            {
                if (e.Before.Length == n && got == e.Before)
                {
                    hwp.Run("Delete");
                    ComHelpers.InsertText(hwp, e.After);
                    log($"  [상용구] ✔ '{e.Before}' → '{e.After}'");
                    return true;
                }
            }
        }
        return false;
    }
}
