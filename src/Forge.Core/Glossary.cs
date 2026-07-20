// 상용구(glossary) — 준말(before) → 본말(after) 치환 규칙.
//
// 저장: %APPDATA%\Forge\settings.json 의 "glossary" 섹션, key "entries" =
//   [ { "before": "...", "after": "..." }, ... ] (순서 보존 배열).
// 미설정(첫 실행)이면 Defaults 5종 반환.
//
// 사용처:
//   - Ctrl+Shift+I (glossary_expand) → GlossaryExpand.ExpandAtCaret
//   - 탭 ④ 상용구 (GlossaryTab) 편집 UI

using System.Text.Json;

namespace Forge.Core;

/// <summary>상용구 한 항목 — Before(준말) 를 After(본말) 로 치환.</summary>
public readonly record struct GlossaryEntry(string Before, string After);

public static class Glossary
{
    /// <summary>기본 5종 (사용자 실측 상용구 등록 세트).</summary>
    public static readonly IReadOnlyList<GlossaryEntry> Defaults = new[]
    {
        new GlossaryEntry(".",  "·"),
        new GlossaryEntry("ㅈ", "§"),
        new GlossaryEntry(">",  "→"),
        new GlossaryEntry("ㅁ", "□"),
        new GlossaryEntry("ㅇ", "◦"),
    };

    private const string Section = "glossary";
    private const string Key = "entries";

    // 메모리 캐시 — 매 Ctrl+Shift+I 입력마다 settings.json 디스크 읽기+JSON 파싱을
    // 피한다(핫패스). glossary 섹션의 유일한 writer 가 이 클래스(Save/ResetToDefaults)
    // 라 Save 시 캐시를 갱신하면 항상 최신. 접근은 전부 UI 스레드라 lock 불필요.
    private static List<GlossaryEntry>? _cache;

    /// <summary>저장된 상용구 로드. 섹션 없거나 비면 Defaults 복사본. (캐시됨)</summary>
    public static List<GlossaryEntry> Load()
    {
        _cache ??= LoadFromDisk();
        // 방어적 복사 — 호출부(GlossaryTab 등)가 반환 리스트를 변경해도 캐시 불변.
        return new List<GlossaryEntry>(_cache);
    }

    private static List<GlossaryEntry> LoadFromDisk()
    {
        var section = UserSettings.GetSection(Section);
        if (!section.TryGetValue(Key, out var raw) || raw.ValueKind != JsonValueKind.Array)
            return new List<GlossaryEntry>(Defaults);

        var list = new List<GlossaryEntry>();
        foreach (var el in raw.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            string? before = el.TryGetProperty("before", out var b) && b.ValueKind == JsonValueKind.String
                ? b.GetString() : null;
            string? after = el.TryGetProperty("after", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString() : null;
            if (!string.IsNullOrEmpty(before) && after is not null)
                list.Add(new GlossaryEntry(before, after));
        }
        // 저장은 됐지만 유효 항목이 0개면(전부 지움) 빈 목록 그대로 존중.
        return list;
    }

    /// <summary>상용구 저장 (빈 before 항목은 제외). 성공 시 true. 캐시도 즉시 갱신.</summary>
    public static bool Save(IEnumerable<GlossaryEntry> entries)
    {
        // 저장될(그리고 다음 Load 가 돌려줄) 유효 항목 집합.
        var valid = entries
            .Where(e => !string.IsNullOrEmpty(e.Before) && e.After is not null)
            .ToList();
        var arr = valid
            .Select(e => (object?)new Dictionary<string, object?>
            {
                ["before"] = e.Before,
                ["after"]  = e.After,
            })
            .ToList();
        bool ok = UserSettings.UpdateSection(Section, new Dictionary<string, object?> { [Key] = arr });
        if (ok) _cache = valid;   // 디스크 재읽기 없이 캐시 갱신
        return ok;
    }

    /// <summary>Defaults 로 되돌리고 저장.</summary>
    public static bool ResetToDefaults() => Save(Defaults);
}
