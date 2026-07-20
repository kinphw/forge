// Forge 단축키 가능한 action 카탈로그 — single source of truth.
// Python 원본 forge/ui/actions.py 1:1.
//
// ★ Id 는 영속화 안정 키 — UserSettings.json keymap lookup. 절대 변경 금지.
// ★ ACTIONS 의 순서가 hk_id (1-indexed). 새 action 은 끝에만 추가.

namespace Forge.UI.Tabs;

public sealed record ActionDef(
    string Id,
    string DefaultKey,
    string Label,
    Action<RealtimeTab> Invoke);

public static class Actions
{
    public static readonly IReadOnlyList<ActionDef> All = new ActionDef[]
    {
        new("auto_align",       "Q", "자동 정렬",            rt => rt.RunAutoAlign()),
        new("word_pull",        "W", "어절 끌어올림",        rt => rt.RunWordPull()),
        new("font_body",        "A", "본문 폰트",            rt => rt.ApplyFont(rt.Font1Name, rt.Font1Size)),
        new("font_annotation",  "S", "주석 폰트",            rt => rt.ApplyFont(rt.Font2Name, rt.Font2Size)),
        new("font_headline",    "F", "헤드라인 폰트",        rt => rt.ApplyFont(rt.Font3Name, rt.Font3Size)),
        new("font_uleungdo",    "G", "울릉도 폰트",          rt => rt.ApplyFont(rt.Font4Name, rt.Font4Size)),
        new("para_size_8",      "D", "현재 문단 글자크기",   rt => rt.RunParagraphSize8()),
        new("kerning_reset",    "Z", "자간 0",               rt => rt.RunKerningReset()),
        new("md_convert_sel",   "X", "선택→md 변환",         rt => rt.RunMdConvertSelection()),
        new("margin_capture",   "",  "여백 캡쳐",            rt => rt.RunMarginCapture()),
        new("margin_apply",     "",  "여백 적용",            rt => rt.RunMarginApply()),
        new("char_width_ratio", "E", "현재 문단 장평",       rt => rt.RunCharWidthRatio()),
        // I — 기본키가 C 였으나 한/글 Ctrl+Shift+C(가운데 정렬) 와 충돌해 교체 (2026-07-16).
        new("glossary_expand",  "I", "상용구 확장",          rt => rt.RunGlossaryExpand()),
    };

    public static ActionDef ByHkId(int hkId) => All[hkId - 1];
}
