// 자동 들여쓰기 정렬 — tool1 `super_shift_tab_text` 정확 포트.
// Python 원본 forge/linter/indent_align.py 의 1:1 포팅.
//
// 알고리즘 (tool1 super_shift_tab_text):
//   1. ParaBegin / ParaEnd 줄 번호 비교 — 같으면 한 줄, skip
//      (KeyIndicator out 파라미터 회피 — line 차이 측정 helper 우회)
//   2. ParaBegin → MoveSelNextWord → block_char → c[:-1]
//   3. (Forge 추가) 첫 워드가 bullet/annotation marker 인지 검증.
//   4. while: 본문 워드 (alpha 있고 마커 아님) 발견까지 워드 점프
//   5. MoveWordBegin → ParagraphShapeIndentAtCaret
//   6. MoveNextParaBegin

using System.Globalization;

namespace Forge.Core.Linter;

public static class IndentAlign
{
    // ────────────────────────────────────────────────────────────────────
    // 마커 판별 (Python 원본 1:1)
    // ────────────────────────────────────────────────────────────────────

    // bullet markers — md 입력 + BulletRenderer 출력 모두 포함
    private static readonly HashSet<string> BulletMarkers = new()
    {
        "□",   // U+25A1
        "○",   // U+25CB (md 입력)
        "◦",   // U+25E6 (L2 출력)
        "-",   // U+002D
        "·",   // U+00B7
        "⇨",   // U+21E8 — 결론 박스
        "→",   // U+2192 — 결론 화살표 변형
    };

    private static readonly HashSet<string> AnnotationFixed = new() { "※", "†" };

    // 섹션·소제목 prefix 문자집합
    private static readonly HashSet<char> SectionHangul =
        new("가나다라마바사아자차카타파하".ToCharArray());
    private static readonly HashSet<char> SectionRoman =
        new("ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩⅪⅫ".ToCharArray());

    private static readonly HashSet<char> NumberTokenChars = new() { '.', '(', ')', '[', ']', '{', '}', '-' };

    public static bool IsBulletOrAnnotationMarker(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        if (BulletMarkers.Contains(word)) return true;
        if (AnnotationFixed.Contains(word)) return true;
        // 모든 글자가 '*' 인 경우 (annotation ref: */**/***)
        if (word.All(ch => ch == '*')) return true;
        return false;
    }

    public static bool IsSectionMarker(string word)
    {
        if (string.IsNullOrEmpty(word) || !word.EndsWith('.')) return false;
        var prefix = word[..^1];
        if (prefix.Length == 0) return false;
        if (prefix.All(char.IsDigit)) return true;
        if (prefix.All(ch => SectionHangul.Contains(ch))) return true;
        if (prefix.All(ch => SectionRoman.Contains(ch))) return true;
        return false;
    }

    public static bool IsSkipMarker(string word) =>
        IsBulletOrAnnotationMarker(word) || IsSectionMarker(word);

    /// <summary>
    /// 본문 워드 판정 — '마커 / 순수 번호 토큰' 이 아닌 모든 워드 = body.
    /// </summary>
    public static bool IsBodyWord(string c)
    {
        if (string.IsNullOrEmpty(c)) return false;
        if (IsSkipMarker(c)) return false;

        foreach (var ch in c)
        {
            if (char.IsLetter(ch)) return true;       // 한글/라틴/한자 등
            if (char.IsWhiteSpace(ch)) continue;
            if (char.IsDigit(ch)) continue;
            if (NumberTokenChars.Contains(ch)) continue;
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.OtherNumber or UnicodeCategory.LetterNumber)
                continue;                              // ① ㉠ Ⅰ 등 enclosed number
            return true;                               // Po/So/Sm 등 (§, #, +, =, ~)
        }
        return false;
    }

    // ────────────────────────────────────────────────────────────────────
    // InitScan / GetText helpers (block_char 등가)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>선택 영역의 텍스트. InitScan + GetText + ReleaseScan.</summary>
    internal static string BlockChar(dynamic hwp)
    {
        try
        {
            // Python: hwp.InitScan(option=None, Range=0xff, spara=None, spos=None, epara=None, epos=None)
            // C# dynamic: positional 인자 (모든 None → null, Range=0xff)
            hwp.InitScan(null, 0xff, null, null, null, null);
            // GetText 는 (success_flag, text) 또는 비슷한 tuple 반환 — dynamic 으로 수신
            var result = hwp.GetText();
            try { hwp.ReleaseScan(); } catch { /* skip */ }

            if (result is null) return "";
            // result 가 tuple-like 면 result[1] 또는 result.Item(1) 등으로 추출
            try
            {
                var text = result[1];
                return text?.ToString() ?? "";
            }
            catch { /* indexer 실패 */ }
            try
            {
                return result.ToString() ?? "";
            }
            catch { return ""; }
        }
        catch
        {
            try { hwp.ReleaseScan(); } catch { /* skip */ }
            return "";
        }
    }

    internal static int BlockLen(dynamic hwp) => BlockChar(hwp).Length;

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s.Replace("\n", "\\n") : s[..max].Replace("\n", "\\n") + "...";

    // ────────────────────────────────────────────────────────────────────
    // line 측정 — KeyIndicator out 파라미터 회피
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// caret 의 현재 줄 번호 추정.
    /// KeyIndicator out 파라미터 우회 — 절대 줄 번호 못 얻으니, 한 가지만 가능한
    /// "두 위치 사이 줄 수 차이" 는 LineDiff() 로 측정. 단순 "한 줄짜리 문단" 판정은
    /// ParaBegin → MoveLineEnd 후 위치 비교로 대체.
    ///
    /// 이 메서드는 caret 위치를 변경하지 않음 (Get/SetCaretPos 로 복원).
    /// </summary>
    internal static int LineNumberApprox(dynamic hwp)
    {
        // 추정 — pos 값 자체 (글자 단위). 같은 줄에선 pos 단조 증가, 다음 줄로 가면 reset.
        // 절대 line 번호는 아니지만 같은 문단 내 비교용으론 충분.
        var p = Range.GetCaretPos(hwp);
        return p.Pos;
    }

    /// <summary>한 줄짜리 문단인지 — ParaBegin 과 ParaEnd 의 caret 위치 비교.</summary>
    internal static bool IsSingleLineParagraph(dynamic hwp)
    {
        hwp.Run("MoveParaBegin");
        var begin = Range.GetCaretPos(hwp);
        hwp.Run("MoveParaEnd");
        var end = Range.GetCaretPos(hwp);
        hwp.Run("MoveParaBegin");
        // 같은 문단 내 ParaBegin/End 가 같은 줄에 있는지 — 정확한 line 번호 없이는
        // 어렵지만, 짧은 문단 (예: pos 차이 < 20) 은 보통 한 줄 추정.
        // 정확한 판정은 한컴 환경별로 동작 차이가 있어 LineDiff 같은 dispatch 측정 필요.
        // 일단 보수적으로 "ParaBegin==ParaEnd 라면 한 줄" (정확 비교).
        return begin == end;
    }

    // ────────────────────────────────────────────────────────────────────
    // 문단 1개 처리
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// tool1 super_shift_tab_text 정확 포트 + Forge marker 검증.
    /// 한 문단 처리 후 caret 은 다음 문단 시작 부근.
    /// </summary>
    public static void ProcessParagraph(object hwpObj, LogFn? log = null)
    {
        dynamic hwp = hwpObj;
        log ??= _ => { };

        // 한 줄 문단 검사 — ParaBegin/End caret 비교
        hwp.Run("MoveParaBegin");
        var paraBeginPos = Range.GetCaretPos(hwp);
        hwp.Run("MoveParaEnd");
        var paraEndPos = Range.GetCaretPos(hwp);
        hwp.Run("MoveParaBegin");

        if (paraBeginPos == paraEndPos)
        {
            hwp.Run("MoveNextParaBegin");
            return;
        }

        // ★ Fast skip — 문단 전체 텍스트 한 번에 추출. 비어있으면 즉시 skip.
        // (이전: MoveSelNextWord 10회 + BlockChar 10회 dispatch 후에야 빈 문단 판정.
        //  C# dynamic dispatch 의 IPC overhead 큰 환경에서 시각적으로 부산스러움.)
        hwp.Run("MoveSelParaEnd");
        var full = BlockChar(hwp);
        hwp.Run("Cancel");
        hwp.Run("MoveParaBegin");

        if (string.IsNullOrWhiteSpace(full))
        {
            log($"  [line] empty para — fast skip");
            hwp.Run("MoveNextParaBegin");
            return;
        }
        log($"  [line] paraBegin={paraBeginPos} paraEnd={paraEndPos} text={Truncate(full, 40)}");

        // 첫 텍스트 워드 검색 — FWS only 워드 (빈 문자열) 건너뜀
        string? firstTextWord = null;
        bool isMarkerKind = false;
        for (int trial = 0; trial < 10; trial++)
        {
            hwp.Run("MoveSelNextWord");
            var cRaw = BlockChar(hwp);
            var stripped = cRaw.Trim();
            log($"  [skip-search#{trial}] raw={cRaw} stripped={stripped}");
            if (stripped.Length == 0)
            {
                hwp.Run("Cancel");
                continue;
            }
            firstTextWord = stripped;
            isMarkerKind = IsSkipMarker(stripped);
            log($"  [first-text] word={stripped} is_marker={isMarkerKind}");
            hwp.Run("Cancel");
            break;
        }
        if (firstTextWord is null)
        {
            log("  → 10 시도 후 텍스트 워드 못 찾음 — skip");
            hwp.Run("MoveNextParaBegin");
            return;
        }

        // 본문 워드 검색
        hwp.Run("MoveParaBegin");
        hwp.Run("MoveSelNextWord");
        var c2Raw = BlockChar(hwp);
        var c2 = c2Raw.Length > 0 ? c2Raw[..^1] : "";
        log($"  [body-search#0] raw={c2Raw} after[:-1]={c2} body={IsBodyWord(c2)}");

        int iters = 0;
        bool found = false;
        CaretPos? lastPos = null;
        while (iters < 30)
        {
            if (IsBodyWord(c2))
            {
                found = true;
                break;
            }
            hwp.Run("Cancel");
            var curPos = Range.GetCaretPos(hwp);
            hwp.Run("MoveSelNextWord");
            var newPos = Range.GetCaretPos(hwp);
            if (curPos == newPos && lastPos == curPos)
            {
                log($"  → 캐럿 진행 멈춤 (pos {curPos} 반복), 끝");
                break;
            }
            lastPos = curPos;
            c2Raw = BlockChar(hwp);
            c2 = c2Raw.Length > 0 ? c2Raw[..^1] : "";
            iters++;
            log($"  [body-search#{iters}] raw={c2Raw} after[:-1]={c2} body={IsBodyWord(c2)}");
        }

        log($"  [loop] body 발견={found} (iters={iters})");
        if (!found)
        {
            log("  → 본문 워드 못 찾음");
            hwp.Run("Cancel");
            hwp.Run("MoveNextParaBegin");
            return;
        }

        // IndentAtCaret
        hwp.Run("MoveWordBegin");
        var posAt = Range.GetCaretPos(hwp);
        log($"  [indent] IndentAtCaret 호출 직전 pos={posAt}");
        hwp.Run("ParagraphShapeIndentAtCaret");
        log("  [indent] ✔ ParagraphShapeIndentAtCaret 호출됨");

        hwp.Run("MoveNextParaBegin");
    }

    // ────────────────────────────────────────────────────────────────────
    // 객체 영역 처리 — 표 셀 등 list >= 2
    // ────────────────────────────────────────────────────────────────────

    private static void ProcessObjects(dynamic hwp, LogFn log)
    {
        int area = 1;
        int objCount = 0;
        while (area < 100_000)
        {
            area++;
            try
            {
                Range.SetCaretPos(hwp, new CaretPos(area, 0, 0));
            }
            catch
            {
                break;
            }
            var cur = Range.GetCaretPos(hwp);
            if (cur.List == 0) break;  // fallback — 더 이상 area 없음

            log($"  [object area={area}] cur={cur}");
            int innerIters = 0;
            while (innerIters < 1000)
            {
                var start = Range.GetCaretPos(hwp);
                ProcessParagraph(hwp, log);
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

    // ────────────────────────────────────────────────────────────────────
    // Public API
    // ────────────────────────────────────────────────────────────────────

    /// <summary>본문 + 표 셀 등 객체 영역까지 모두 순회 (배치 모드 STAGE 2).</summary>
    public static void AlignLeftIndent(dynamic hwp, LogFn? log = null)
    {
        log ??= _ => { };

        log("[align_left_indent] 본문 순회 시작");
        hwp.Run("MoveDocEnd");
        var endPos = Range.GetCaretPos(hwp);
        hwp.Run("MoveDocBegin");

        const int maxIter = 100_000;
        int iters = 0;
        while (iters < maxIter)
        {
            var prev = Range.GetCaretPos(hwp);
            if (prev == endPos) break;
            ProcessParagraph(hwp, log);
            var cur = Range.GetCaretPos(hwp);
            if (cur == prev)
            {
                log("  [본문 STOP] 진행 멈춤");
                break;
            }
            iters++;
        }
        log($"[align_left_indent] 본문 처리 완료 ({iters} 문단)");

        log("[align_left_indent] 객체 영역 순회 시작");
        ProcessObjects(hwp, log);
        log("[align_left_indent] 객체 영역 처리 완료");
    }

    /// <summary>selection 있으면 범위, 없으면 현재 문단 1개.</summary>
    public static void AlignCurrentParagraph(dynamic hwp, LogFn? log = null)
    {
        log ??= _ => { };
        log("[align_current_paragraph] 시작");
        // dynamic dispatch 에 method group 직접 넘기기 못함 → delegate cast
        ParaActionFn action = ProcessParagraph;
        Range.ApplyPerParagraph(hwp, action, log);
        log("[align_current_paragraph] 완료");
    }
}
