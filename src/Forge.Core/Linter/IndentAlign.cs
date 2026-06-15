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
using Forge.Core.Renderers;     // Primitives.SetFontSize
using Forge.Interop.HwpObject;  // PIA — typed IHwpObject.GetText(out string)

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
        "◈",   // U+25C8 — 마름모 강조 글머리
        "➡",   // U+27A1 — 굵은 오른쪽 화살표
        "→",   // U+2192 — 결론 화살표 변형
        "☞",   // U+261E — 손가락 화살표 (md 입력) // 260615
        // 원문자 숫자 ①~⑮ (U+2460~U+246E) — 번호 글머리
        "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨", "⑩",
        "⑪", "⑫", "⑬", "⑭", "⑮",
        // 굵은(반전) 원문자 ❶~⓯ (1~10: U+2776~U+277F, 11~15: U+24EB~U+24EF)
        "❶", "❷", "❸", "❹", "❺", "❻", "❼", "❽", "❾", "❿",
        "⓫", "⓬", "⓭", "⓮", "⓯",
        // 이중 원문자 ⓵~⓾ (U+24F5~U+24FE) — 유니코드 정의는 1~10 까지만 존재
        "⓵", "⓶", "⓷", "⓸", "⓹", "⓺", "⓻", "⓼", "⓽", "⓾",
        // 네모(사각형) 숫자 — 한컴 PUA(HFT 전용 글리프, 표준 유니코드 아님).
        //   Forge 는 한/글 위에서만 동작하므로 PUA 마커도 문서 내 정상 인식.
        //   사용자 실측 코드포인트 (Forge.Probe scan, 2026-06-15):
        //     1군 U+F02B0~F02B9 (네모0~9), 2군 U+F02CD~F02D6 (네모 변형)
        "\U000F02B0", "\U000F02B1", "\U000F02B2", "\U000F02B3", "\U000F02B4",
        "\U000F02B5", "\U000F02B6", "\U000F02B7", "\U000F02B8", "\U000F02B9",
        "\U000F02CD", "\U000F02CE", "\U000F02CF", "\U000F02D0", "\U000F02D1",
        "\U000F02D2", "\U000F02D3", "\U000F02D4", "\U000F02D5", "\U000F02D6",
    };

    private static readonly HashSet<string> AnnotationFixed = new() { "※", "†" };

    // 섹션·소제목 prefix 문자집합
    private static readonly HashSet<char> SectionHangul =
        new("가나다라마바사아자차카타파하".ToCharArray());
    private static readonly HashSet<char> SectionRoman =
        new("ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩⅪⅫ".ToCharArray());

    private static readonly HashSet<char> NumberTokenChars = new() { '.', '(', ')', '[', ']', '{', '}', '-' };

    /// <summary>주석 마커 전용 — ※ † 와 모든 글자가 '*' 인 워드 (*/**/***).</summary>
    public static bool IsAnnotationMarker(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        if (AnnotationFixed.Contains(word)) return true;
        if (word.All(ch => ch == '*')) return true;
        return false;
    }

    public static bool IsBulletOrAnnotationMarker(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        if (BulletMarkers.Contains(word)) return true;
        return IsAnnotationMarker(word);
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

    /// <summary>
    /// 선택 영역의 텍스트.
    ///
    /// ★ 1차: PIA typed cast — IHwpObject.GetText(out string) 직접 호출.
    ///   PIA reflection 으로 확인: DispId 10019 = `int GetText(out String& Text)`.
    ///   typed call 이라 out 파라미터 정상 수신 — InitScan+GetText 패턴 복원.
    ///   메모리만 거치므로 한컴 보안 정책 다이얼로그 미발생 (Python 동등).
    ///
    /// ★ 2차 fallback: GetTextFile("UNICODE", "saveblock").
    ///   PIA cast 가 한컴 IDispatch 의 E_NOINTERFACE 로 실패할 수 있음
    ///   (Forge.Core.csproj 주석 참조 — sub-COM 에서 검증된 케이스).
    ///   한컴 docs: "내부적으로 save/SaveBlockAction 호출 — 메모리에서 3~4번
    ///   복사" → disk 접근으로 인식되어 보안 정책 다이얼로그 발동 가능.
    /// </summary>
    internal static string BlockChar(dynamic hwp)
    {
        // 1차: PIA typed cast
        try
        {
            if ((object)hwp is IHwpObject typed)
            {
                typed.InitScan(null, 0xff, null, null, null, null);
                typed.GetText(out string text);
                try { typed.ReleaseScan(); } catch { }
                return text ?? "";
            }
        }
        catch
        {
            try { hwp.ReleaseScan(); } catch { }
        }

        // 2차: GetTextFile (보안 정책 다이얼로그 부작용 가능성)
        try
        {
            return (hwp.GetTextFile("UNICODE", "saveblock") as string) ?? "";
        }
        catch
        {
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
        //
        // ★ string 명시 — hwp 가 dynamic 이라 BlockChar 호출 전체가 dynamic dispatch
        //   되어 반환값도 dynamic 으로 wrap. var 로 받으면 후속 .Length / [..^1] 등
        //   indexer/Range slice 가 dynamic dispatch 되며 RuntimeBinderException.
        //   명시적 string 으로 받아 정적 호출 chain 복원.
        hwp.Run("MoveSelParaEnd");
        string full = BlockChar(hwp);
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
            string cRaw = BlockChar(hwp);
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
        string c2Raw = BlockChar(hwp);
        string c2 = c2Raw.Length > 0 ? c2Raw[..^1] : "";
        log($"  [body-search#0] raw={c2Raw} after[:-1]={c2} body={IsBodyWord(c2)}");

        int iters = 0;
        bool found = false;
        bool annMarkerSeen = false;   // 주석 마커(* ※ †) 를 이미 지났나 (sticky)
        CaretPos? lastPos = null;
        while (iters < 30)
        {
            // ★ 주석 마커(*/※/†) 직후의 워드는 번호 토큰((1) 등) 이라도 본문 시작점으로 간주.
            //   '* (1) 전산원장' → '*' 만 지나 '(1)' 에서 indent (한 단계만 들어감).
            //   글머리(□○-·)는 annMarkerSeen=false 라 기존대로 번호 토큰을 더 건너뛰어
            //   본문(전산원장)까지 들어감 — 동작 변화 없음.
            if (IsBodyWord(c2) || (annMarkerSeen && !string.IsNullOrWhiteSpace(c2)))
            {
                found = true;
                break;
            }
            if (IsAnnotationMarker(c2)) annMarkerSeen = true;
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
            log($"  [body-search#{iters}] raw={c2Raw} after[:-1]={c2} body={IsBodyWord(c2)} annSeen={annMarkerSeen}");
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

    /// <summary>
    /// 본문 (+ 옵션: 표 셀 등 객체 영역) 순회 (배치 모드 STAGE 2).
    /// includeObjects: false 면 객체 영역 skip — 마크다운 변환 후처리용.
    /// 마크다운 박스 셀들은 렌더러가 이미 정확히 배치한 상태라 추가 indent 가 오히려 망침.
    /// </summary>
    public static void AlignLeftIndent(dynamic hwp, LogFn? log = null, bool includeObjects = true)
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

        if (includeObjects)
        {
            log("[align_left_indent] 객체 영역 순회 시작");
            ProcessObjects(hwp, log);
            log("[align_left_indent] 객체 영역 처리 완료");
        }
        else
        {
            log("[align_left_indent] 객체 영역 skip (includeObjects=false)");
        }
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

    // ────────────────────────────────────────────────────────────────────
    // 마커 연속 사이 빈줄 자동 삽입 (Q 자동정렬 pre-pass)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 현재 캐럿 위치의 문단이 마커 문단인지 — 첫 문자/토큰이 bullet/annotation/section 마커.
    /// 호출 전후 캐럿은 현재 문단 시작에 위치.
    ///
    /// ★ 공백 없는 표기 대응 — '□안녕' 같이 마커 직후 공백 없는 케이스도 인식.
    ///   공백 기준 첫 단어 슬라이싱은 '□안녕' → '□안녕' 통째 → marker 미인식 사고.
    ///   1) single-char marker (□ ○ ◦ - · ⇨ → ※ † *) 는 첫 문자만 검사 — 공백 무관.
    ///   2) section marker (1./가./Ⅰ. 등) 는 '.' 까지의 짧은 prefix 추출 + 클래스 검사.
    /// </summary>
    public static bool IsCurrentParagraphMarker(dynamic hwp)
    {
        hwp.Run("MoveParaBegin");
        hwp.Run("MoveSelParaEnd");
        string full = BlockChar(hwp);
        hwp.Run("Cancel");
        hwp.Run("MoveParaBegin");

        if (string.IsNullOrWhiteSpace(full)) return false;
        var trimmed = full.TrimStart();
        if (trimmed.Length == 0) return false;

        // 1) single-char bullet/annotation — 첫 문자만 보면 됨 (공백 없어도 OK)
        string firstCharStr = trimmed[0].ToString();
        if (BulletMarkers.Contains(firstCharStr)) return true;
        if (AnnotationFixed.Contains(firstCharStr)) return true;
        if (trimmed[0] == '*') return true;

        // 2) section marker (digit/hangul/roman prefix + '.') — '.' 위치 짧은 범위 안에서 검색
        int dotIdx = trimmed.IndexOf('.');
        if (dotIdx > 0 && dotIdx <= 4)
        {
            var prefix = trimmed[..dotIdx];
            if (prefix.All(char.IsDigit)) return true;
            if (prefix.All(ch => SectionHangul.Contains(ch))) return true;
            if (prefix.All(ch => SectionRoman.Contains(ch))) return true;
        }

        return false;
    }

    /// <summary>
    /// 현재 문단 앞에 빈 단락 1줄 삽입 + d처리 (글자크기 BlankSize 적용).
    /// 호출 전: 캐럿 = 현재 문단 시작.
    /// 호출 후: 캐럿 = 같은 문단 시작 (현재 문단은 1 칸 밑으로 밀려있음, Para 인덱스 +1).
    ///
    /// BreakPara at start of N → empty new para 위에 생성, caret 은 (shifted) N 시작.
    /// → MovePrevParaBegin 으로 빈 para 진입 → MoveSelParaEnd + SetFontSize + Cancel
    ///   (RunParagraphSize8 와 동일 패턴) → MoveNextParaBegin 으로 N 시작 복귀.
    /// </summary>
    public static void InsertBlankBeforeCurrent(dynamic hwp, double blankSizePt)
    {
        hwp.Run("MoveParaBegin");
        hwp.Run("BreakPara");
        hwp.Run("MovePrevParaBegin");
        hwp.Run("MoveParaBegin");
        hwp.Run("MoveSelParaEnd");
        Primitives.SetFontSize(hwp, blankSizePt);
        hwp.Run("Cancel");
        hwp.Run("MoveNextParaBegin");
    }

    /// <summary>
    /// 현재 selection 의 범위 내에서, 연속된 marker 문단 사이에 빈 단락 (BlankSize pt) 자동 삽입.
    /// 반환: 삽입된 줄 수 — caller 가 새 end.Para = origEnd.Para + 반환값 으로 확장.
    ///
    /// 알고리즘:
    ///   1. SelectionRange 로 (start, origEnd) 획득. 단일 캐럿이면 0 반환 (no-op).
    ///   2. start.List != origEnd.List 이면 skip (cross-list 영역은 보수적으로 건드리지 않음).
    ///   3. 캐럿을 start 로 이동, origParaCount 만큼 forward 순회:
    ///        - 현재 문단 marker 검사
    ///        - prev_was_marker && cur_is_marker → InsertBlankBeforeCurrent
    ///        - prev_was_marker := cur_is_marker
    ///        - MoveNextParaBegin (삽입 직후라도 다음 원본 문단으로 자연 이동)
    ///   4. 빈 단락 / body 문단은 prev_was_marker=false 로 자동 리셋 — 기존 빈줄 있으면 추가 삽입 없음.
    /// </summary>
    public static int InsertBlanksBetweenMarkers(dynamic hwp, double blankSizePt, LogFn? log = null)
    {
        log ??= _ => { };

        object hwpObj = hwp;
        var sel = Range.SelectionRange(hwpObj);
        if (sel is null)
        {
            log("[blank-insert] 단일 캐럿 — skip");
            return 0;
        }

        var start = sel.Value.Start;
        var origEnd = sel.Value.End;
        if (start.List != origEnd.List)
        {
            log($"[blank-insert] cross-list selection ({start.List} ≠ {origEnd.List}) — skip");
            return 0;
        }

        log($"[blank-insert] selection {start} → {origEnd}");
        hwp.Run("Cancel");
        Range.SetCaretPos(hwp, start);

        int origParaCount = origEnd.Para - start.Para + 1;
        if (origParaCount < 2)
        {
            log("[blank-insert] 단일 문단 selection — 비교 대상 없음, skip");
            return 0;
        }

        int inserted = 0;
        int processed = 0;
        bool prevWasMarker = false;
        const int maxIter = 1000;
        int iters = 0;

        while (iters < maxIter && processed < origParaCount)
        {
            bool isMarker = IsCurrentParagraphMarker(hwp);
            var caretAt = Range.GetCaretPos(hwp);
            log($"  [blank-insert #{processed}] caret={caretAt} isMarker={isMarker}");

            if (prevWasMarker && isMarker)
            {
                InsertBlankBeforeCurrent(hwp, blankSizePt);
                inserted++;
                log($"    ✔ 빈줄 삽입 (누적 {inserted})");
            }

            prevWasMarker = isMarker;
            processed++;

            var prevCaret = Range.GetCaretPos(hwp);
            hwp.Run("MoveNextParaBegin");
            var newCaret = Range.GetCaretPos(hwp);
            if (newCaret == prevCaret)
            {
                log("[blank-insert] 진행 멈춤 — 종료");
                break;
            }
            iters++;
        }

        log($"[blank-insert] 완료: 처리 {processed} 문단, 삽입 {inserted} 줄");
        return inserted;
    }
}
