// Forge 사용자 개인 설정 영속화 — %APPDATA%\Forge\settings.json.
// Python 원본 forge/user_settings.py 1:1 포팅.
//
// Windows 표준 위치 (%APPDATA% = C:\Users\<user>\AppData\Roaming) 에 저장.
// 다중 사용자 PC 에서 사용자별 분리 자동, 프로젝트 폴더 깨끗 유지.
// trade-off: portable exe 를 다른 PC 로 옮길 때 설정은 따라오지 않음 (의도).
//
// 저장 항목:
//   - keymap: {action_id: key_letter or null} — 사용자가 변경한 hotkey 만
//     null = 명시 비활성화, key 누락 = ACTIONS 의 default_key fallback
//   - 향후: fonts/sizes, 마지막 선택한 한/글 인스턴스 moniker 등

using System.Text.Json;

namespace Forge.Core;

public static class UserSettings
{
    /// <summary>%APPDATA%\Forge\ — 디렉토리는 lazy 생성.</summary>
    public static string SettingsDir()
    {
        var appdata = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrEmpty(appdata))
            return Path.Combine(appdata, "Forge");
        // APPDATA 없는 비정상 Windows / 비-Windows fallback
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".forge");
    }

    public static string SettingsPath() => Path.Combine(SettingsDir(), "settings.json");

    /// <summary>settings.json 로드. 파일 없거나 손상 시 빈 dict (silent).</summary>
    public static Dictionary<string, JsonElement> Load()
    {
        var path = SettingsPath();
        if (!File.Exists(path)) return new();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new();
        }
    }

    /// <summary>settings.json 저장. write 실패 시 false (권한/디스크 풀).</summary>
    public static bool Save(Dictionary<string, JsonElement> data)
    {
        try
        {
            var dir = SettingsDir();
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            File.WriteAllText(SettingsPath(), json);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // ─ section helpers ─────────────────────────────────────────────────

    public static Dictionary<string, JsonElement> GetSection(string name)
    {
        var data = Load();
        if (!data.TryGetValue(name, out var raw) || raw.ValueKind != JsonValueKind.Object)
            return new();
        var result = new Dictionary<string, JsonElement>();
        foreach (var p in raw.EnumerateObject())
            result[p.Name] = p.Value.Clone();
        return result;
    }

    public static bool UpdateSection(string name, Dictionary<string, object?> updates)
    {
        var data = Load();
        Dictionary<string, object?> section;
        if (data.TryGetValue(name, out var raw) && raw.ValueKind == JsonValueKind.Object)
        {
            section = new();
            foreach (var p in raw.EnumerateObject())
                section[p.Name] = JsonElementToObject(p.Value);
        }
        else
        {
            section = new();
        }
        foreach (var (k, v) in updates)
            section[k] = v;

        var dataPlain = new Dictionary<string, object?>();
        foreach (var (k, v) in data)
            dataPlain[k] = JsonElementToObject(v);
        dataPlain[name] = section;

        // 재직렬화로 dict<string, JsonElement> 형태 만듦
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dataPlain);
        var redeserialized = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes) ?? new();
        return Save(redeserialized);
    }

    public static bool SetSectionEntry(string name, string key, object? value) =>
        UpdateSection(name, new Dictionary<string, object?> { [key] = value });

    // ─ keymap helpers ──────────────────────────────────────────────────

    /// <summary>
    /// 저장된 keymap — {action_id: key_letter or null}.
    /// 검증: 1글자 영문/숫자 (alpha or digit). 비검증 항목은 누락.
    /// </summary>
    public static Dictionary<string, string?> GetKeymap()
    {
        var section = GetSection("keymap");
        var result = new Dictionary<string, string?>();
        foreach (var (k, v) in section)
        {
            if (v.ValueKind == JsonValueKind.Null)
            {
                result[k] = null;
            }
            else if (v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (s?.Length == 1 && (char.IsLetter(s[0]) || char.IsDigit(s[0])))
                    result[k] = s.ToUpperInvariant();
            }
        }
        return result;
    }

    /// <summary>keymap 한 항목 수정 후 즉시 flush. key=null 이면 비활성화 저장.</summary>
    public static bool SetKeymapEntry(string actionId, string? key)
    {
        var value = key is null ? (object?)null : key.ToUpperInvariant();
        return UpdateSection("keymap", new Dictionary<string, object?> { [actionId] = value });
    }

    // ─ internal ────────────────────────────────────────────────────────

    private static object? JsonElementToObject(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? (object)l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Select(JsonElementToObject).ToList(),
        _ => null,
    };
}
