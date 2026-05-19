// 한/글 COM API 호출 헬퍼.
//
// Python 원본 forge/com_helpers.py 의 1:1 포팅.
// tool2 의 wrapper 411개를 그대로 답습하지 않고 5단계 패턴
// (CreateAction → CreateSet → GetDefault → SetItem → Execute) 을
// 1줄 함수로 묶는다. 룰 코드는 액션명을 문자열로 직접 작성 — hwp-api-mcp
// / tool2-spec-mcp 검색 결과를 그대로 옮길 수 있어 self-documenting.
//
// dynamic 사용 이유:
//   - 한/글 COM 의 ParameterSet 은 IDispatch late-binding 으로만 호출 가능
//   - tlbimp 정적 타이핑 불필요 (사실상 모든 호출이 문자열 키 기반)
//   - pywin32 의 win32com.client 와 호출 syntax 가 거의 동일 — 포팅 직접성 ★

using System.Collections.Generic;

namespace Forge.Core;

public static class ComHelpers
{
    /// <summary>
    /// 5단계 COM 패턴을 1줄 호출로.
    ///
    /// 예 (tool2 의 자간헌터와 등가):
    ///     ComHelpers.SetParam(hwp, "ParagraphShape", new() { ["BreakNonLatinWord"] = 0 });
    ///
    /// 예 (tool2 의 줄간격 등가):
    ///     ComHelpers.SetParam(hwp, "ParagraphShape", new() {
    ///         ["LineSpacingType"] = 0,
    ///         ["LineSpacing"] = 150,
    ///     });
    /// </summary>
    public static void SetParam(dynamic hwp, string action, IReadOnlyDictionary<string, object> items)
    {
        var act = hwp.CreateAction(action);
        var s = act.CreateSet();
        act.GetDefault(s);
        foreach (var (k, v) in items)
        {
            try
            {
                s.SetItem(k, v);
            }
            catch (Exception ex)
            {
                // 진단 — 어떤 key/타입에서 한컴이 거부했는지 메시지에 노출.
                throw new InvalidOperationException(
                    $"SetItem 실패: action={action} key={k} " +
                    $"valueType={v?.GetType().FullName ?? "null"} value={v}",
                    ex);
            }
        }
        act.Execute(s);
    }

    /// <summary>현재 위치에 텍스트 삽입 (tool2 '문장' 메서드 등가).</summary>
    public static void InsertText(dynamic hwp, string text) =>
        SetParam(hwp, "InsertText", new Dictionary<string, object> { ["Text"] = text });

    /// <summary>매개변수 없는 단순 액션 실행 (BreakPara, MoveRight, Cancel 등).</summary>
    public static void Run(dynamic hwp, string action) => hwp.HAction.Run(action);

    /// <summary>mm → HWP 단위 변환 (HWP 내부 단위 = 1/7200 inch).</summary>
    public static int MmToHwp(dynamic hwp, double mm) => (int)hwp.MiliToHwpUnit(mm);

    /// <summary>
    /// pt → HWP 단위 변환.
    ///
    /// tool2 관례에 따라 모든 pt 값에 *2 적용 후 PointToHwpUnit 호출.
    /// (재현성을 위해 같은 관례 유지 — 왜 *2 인지는 tool2 원본 주석에 미상)
    /// </summary>
    public static int PtToHwp(dynamic hwp, double pt) => (int)hwp.PointToHwpUnit(pt * 2);

    /// <summary>RGB 색상 → HWP COM RGBColor.</summary>
    public static int Rgb(dynamic hwp, int r, int g, int b) => (int)hwp.RGBColor(r, g, b);
}
