// 한/글 COM 인스턴스 attach·신규 생성 헬퍼.
//
// Python 원본 forge/hwp_session.py 의 1:1 포팅.
//
// 연결 정책 (한컴 공식 답변 기반):
//   1. ole32 의 GetRunningObjectTable 로 ROT enumerate
//   2. moniker 이름이 `!HwpObject.{버전}.{인덱스}` 패턴인 항목 탐색
//      버전 코드 ↔ 한/글 출시 버전 (한컴 공식 답변):
//         80  = 한/글 2010
//         90  = 한/글 2014
//         96  = 한컴오피스 NEO
//         100 = 한/글 2018
//         110 = 한/글 2020
//         120 = 한/글 2022
//         130 = 한/글 2024
//      인덱스: 1~99, 같은 버전이 여러 개 떠 있을 때 부여
//   3. 발견 시: rot.GetObject(moniker) → dynamic 으로 받아 attach
//      (.NET 8 에는 Marshal.GetActiveObject 가 제거되어 ROT 직접 접근이 정공법)
//   4. 없으면 (신규 spawn 정책 — DRM 환경 우선):
//      a) 레지스트리에서 Hwp.exe 절대 경로 확보 → Process.Start(UseShellExecute=true)
//         로 사용자 클릭과 동등하게 실행 → ROT 등록 polling 후 attach.
//         Fasoo 등 사내 DRM 이 자격증명 inject 가능 (CoCreate 는 부모=Forge.exe
//         라 inject 회피됨 → 생성 문서 편집 불가).
//      b) 실패 시 fallback: Activator.CreateInstance(Type.GetTypeFromProgID("HWPFrame.HwpObject"))
//         (DRM 미적용 환경에서만 정상)
//
// ★ 정규식 패턴은 위 모든 버전 코드를 동일하게 매칭 (\d+ 사용) — 새 버전이
// 나와도 코드 변경 없이 작동. 매칭된 버전 코드는 HwpSession 에 보관하여
// status 메시지·로그에 노출.
//
// 제약: ROT 는 동일 integrity level 안에서만 enumerate 가능 (Windows COM 정책).
// 즉 Forge 가 High IL (admin) 로 실행되면 Medium IL (일반) 사용자 한/글에는
// attach 불가. 폐쇄망 일반 사용 시나리오에서는 보통 둘 다 Medium IL 이라 작동.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Forge.Core;

// ============================================================================
// 도메인 모델
// ============================================================================

/// <summary>한/글 COM 인스턴스 wrapper.</summary>
public sealed record HwpSession(
    dynamic Hwp,
    bool IsNew,
    bool PreExisting = false,
    int? VersionCode = null,
    int? InstanceIndex = null,
    string? MonikerName = null)
{
    public string VersionName => HwpSessionHelpers.VersionName(VersionCode);
}

/// <summary>
/// ROT 에서 발견된 한/글 인스턴스 1개 (선택 UI 용).
///
/// <see cref="HwpSessionHelpers.ListInstances"/> 로 후보를 수집하고, 사용자가 고른
/// 인스턴스를 <see cref="HwpSessionHelpers.AttachToInstance"/> 로 정식 HwpSession 으로
/// 승격한다. moniker 문자열이 식별자 역할 — 동일 PC 에 같은 버전 한/글이
/// 여러 개 떠 있어도 InstanceIndex 로 구분.
/// </summary>
public sealed record HwpInstance(
    dynamic Hwp,
    string MonikerName,
    int VersionCode,
    int InstanceIndex,
    string ActiveFilePath)
{
    public string VersionName => HwpSessionHelpers.VersionName(VersionCode);

    /// <summary>UI 라벨 — "한/글 2024 #1 — report.hwpx" 형식.</summary>
    public string DisplayLabel
    {
        get
        {
            var fileName = string.IsNullOrEmpty(ActiveFilePath)
                ? "(새 문서 / 저장 안 됨)"
                : Path.GetFileName(ActiveFilePath);
            return $"{VersionName} #{InstanceIndex} — {fileName}";
        }
    }
}

public sealed class NoExistingHwpException(string message) : Exception(message);

/// <summary>
/// 여러 한/글 인스턴스가 떠 있는데 사용자가 아직 선택하지 않은 상태.
///
/// Forge 가 임의로 첫 매칭에 attach 하면 사용자가 의도하지 않은 한/글에서
/// 편집되는 사고 발생 (실제 보고된 증상). 이 예외를 받아 UI 가 picker
/// 다이얼로그를 띄워 사용자에게 명시 선택을 요구해야 함.
/// </summary>
public sealed class MultipleHwpInstancesException(IReadOnlyList<HwpInstance> instances)
    : Exception($"한/글 인스턴스가 {instances.Count}개 떠 있어 자동 선택할 수 없습니다. " +
                "'한/글 선택' 버튼으로 작업할 인스턴스를 골라주세요.")
{
    public IReadOnlyList<HwpInstance> Instances { get; } = instances;
}

// ============================================================================
// Public API — Python 의 모듈 함수와 동일한 의도
// ============================================================================

public static class HwpSessionHelpers
{
    // ─ 한/글 ROT moniker 패턴 — 한컴 공식 답변 기준
    // `!HwpObject.{버전}.{인덱스}` — 버전·인덱스를 capture 해서 식별 정보로 활용
    private static readonly Regex HwpObjectMonikerRegex =
        new(@"^!HwpObject\.(\d+)\.(\d+)$", RegexOptions.Compiled);

    // ─ 버전 코드 ↔ 출시 버전 이름 (한컴 공식 답변)
    // 알 수 없는 코드는 "한/글 v{code}" 로 fallback (새 버전 출시 대응)
    private static readonly Dictionary<int, string> VersionNames = new()
    {
        [80]  = "한/글 2010",
        [90]  = "한/글 2014",
        [96]  = "한컴오피스 NEO",
        [100] = "한/글 2018",
        [110] = "한/글 2020",
        [120] = "한/글 2022",
        [130] = "한/글 2024",
    };

    // ─ 한/글 실행파일 후보 (대소문자 무시 비교)
    private static readonly HashSet<string> HwpProcessNames =
        new(StringComparer.OrdinalIgnoreCase) { "Hwp", "Hword" };

    /// <summary>버전 코드를 사람이 읽는 이름으로. 미상은 일반 fallback.</summary>
    public static string VersionName(int? versionCode) => versionCode switch
    {
        null => "한/글 (버전 미상)",
        int v when VersionNames.TryGetValue(v, out var name) => name,
        int v => $"한/글 v{v}",
    };

    /// <summary>시스템에 한/글 프로세스가 떠 있는지.</summary>
    public static bool IsHwpRunning() => HwpProcessIds().Count > 0;

    /// <summary>
    /// 잡고 있던 COM 객체가 아직 살아 있는지.
    /// 한/글 GUI 가 종료된 뒤 우리 핸들로 RPC 호출하면 실패. 가벼운 속성
    /// 접근으로 liveness 확인.
    /// </summary>
    public static bool IsAlive(dynamic? hwp)
    {
        if (hwp is null) return false;
        try
        {
            _ = hwp.XHwpWindows.Count;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ROT 에 등록된 모든 살아있는 한/글 인스턴스를 수집.
    ///
    /// 각 인스턴스마다 hwp.Path 를 읽어 활성 문서 파일 경로를 얻는다 (저장
    /// 안 된 새 문서는 빈 문자열).
    ///
    /// UI 의 "한/글 선택" 다이얼로그가 표시할 후보 목록 생성용.
    /// </summary>
    public static List<HwpInstance> ListInstances()
    {
        var result = new List<HwpInstance>();
        foreach (var (hwp, name, versionCode, instanceIndex) in EnumerateLiveHwp())
        {
            string path;
            try { path = (hwp.Path ?? "").ToString() ?? ""; }
            catch { path = ""; }
            result.Add(new HwpInstance(hwp, name, versionCode, instanceIndex, path));
        }
        return result;
    }

    /// <summary>
    /// 사용자가 선택한 인스턴스를 정식 세션으로 승격.
    /// is_new=False — Visible 토글 호출 안 함 (사용자가 띄운 윈도우 크기 보존).
    /// </summary>
    public static HwpSession AttachToInstance(HwpInstance instance)
    {
        try { instance.Hwp.RegisterModule("FilePathCheckDLL", "FilePathCheckerModule"); }
        catch { /* 모듈 미등록 환경 — 무시하고 진행 */ }
        return new HwpSession(
            Hwp: instance.Hwp,
            IsNew: false,
            PreExisting: false,
            VersionCode: instance.VersionCode,
            InstanceIndex: instance.InstanceIndex,
            MonikerName: instance.MonikerName);
    }

    /// <summary>
    /// 한/글 COM 연결 — ROT attach 우선, 없으면 신규 spawn.
    /// </summary>
    /// <param name="visible">연결 후 한/글 창을 보이게 할지 (is_new 일 때만 호출).</param>
    /// <param name="allowSpawn">
    /// true (기본): 떠 있는 한/글 없으면 ShellExecute 또는 CoCreate 로 spawn.
    /// false: 떠 있는 한/글이 없으면 즉시 <see cref="NoExistingHwpException"/>.
    ///   ★ Fasoo DRM + 한/글 2022 시작 화면 조합에서 자동 spawn 이 불안정
    ///   → 운영 환경(GUI) 은 false 로 호출하여 사용자에게 한/글 수동 실행 강제.
    /// </param>
    /// <param name="preferMoniker">
    /// 지정 시 그 moniker 와 정확히 일치하는 인스턴스만 attach 시도.
    /// ROT 에 그 moniker 가 없으면 NoExistingHwpException 즉시 raise (silent
    /// fallback 금지 — 사용자가 명시 선택한 한/글 보호).
    /// </param>
    public static HwpSession AttachOrCreate(
        bool visible = true,
        bool allowSpawn = true,
        string? preferMoniker = null)
    {
        // 한/글이 시스템에 떠 있는지 사전 확인 (pre_existing 판정용)
        var hadHwpBefore = IsHwpRunning();

        // 1. ROT enum 으로 기존 인스턴스 attach 시도
        var found = FindInRot(preferMoniker);
        if (found is null && preferMoniker is not null)
        {
            // 사용자가 명시 선택한 인스턴스가 사라짐 — silent fallback 금지.
            throw new NoExistingHwpException(
                $"선택하셨던 한/글 인스턴스 ({preferMoniker}) 가 ROT 에서 사라졌습니다. " +
                "한/글이 종료되었거나 보안 정책으로 ROT 등록이 해제됐을 수 있습니다. " +
                "'한/글 선택' 버튼으로 다시 인스턴스를 골라주세요.");
        }

        bool isNew = false;
        dynamic hwp;
        string? monikerName;
        int? versionCode;
        int? instanceIndex;

        if (found is { } existing)
        {
            (hwp, monikerName, versionCode, instanceIndex) =
                (existing.Hwp, existing.MonikerName, (int?)existing.VersionCode, (int?)existing.InstanceIndex);
        }
        else if (!allowSpawn)
        {
            throw new NoExistingHwpException(
                "떠 있는 한/글 인스턴스를 찾지 못했습니다. " +
                "한/글을 먼저 직접 실행해주세요 (빈 새 문서 또는 임의의 hwpx 파일을 연 상태).");
        }
        else
        {
            // 2. 신규 spawn — ShellExecute 로 Hwp.exe 직접 실행 우선 (DRM 호환).
            var spawned = SpawnViaShell();
            if (spawned is { } s)
            {
                (hwp, monikerName, versionCode, instanceIndex) =
                    (s.Hwp, (string?)s.MonikerName, (int?)s.VersionCode, (int?)s.InstanceIndex);
            }
            else
            {
                // 3. Fallback — Hwp.exe 못 찾거나 실행 실패. CoCreate 경로.
                //    DRM 미적용 환경에서는 정상, 적용 환경에서는 권한 문제 가능성.
                var before = ListRotMonikers();
                var progId = Type.GetTypeFromProgID("HWPFrame.HwpObject")
                    ?? throw new InvalidOperationException(
                        "HWPFrame.HwpObject ProgID 를 찾을 수 없습니다. 한/글이 설치되어 있나요?");
                hwp = Activator.CreateInstance(progId)!;
                monikerName = null;
                versionCode = null;
                instanceIndex = null;
                foreach (var nm in ListRotMonikers().Except(before))
                {
                    var m = HwpObjectMonikerRegex.Match(nm);
                    if (!m.Success) continue;
                    monikerName = nm;
                    versionCode = int.Parse(m.Groups[1].Value);
                    instanceIndex = int.Parse(m.Groups[2].Value);
                    break;
                }
            }
            isNew = true;
        }

        // "한/글 떠 있었지만 attach 불가" 판정 (IL 분리 또는 외부 한/글 ROT 미등록)
        var preExisting = hadHwpBefore && isNew;

        // 한컴 보안 승인 모듈 — 자동화 API 의 파일 접근 다이얼로그 차단.
        try { hwp.RegisterModule("FilePathCheckDLL", "FilePathCheckerModule"); }
        catch { /* 미등록 환경 — 무시 */ }

        // Visible=true 는 신규 spawn 시에만 호출.
        // ROT attach 경로는 사용자가 이미 띄운 윈도우라 재호출하면 geometry reset 부작용 발생.
        if (visible && isNew)
        {
            try { hwp.XHwpWindows.Item(0).Visible = true; }
            catch { /* visible 토글 실패 — 무시 (한/글이 살아있는지만 중요) */ }
        }

        return new HwpSession(
            Hwp: hwp,
            IsNew: isNew,
            PreExisting: preExisting,
            VersionCode: versionCode,
            InstanceIndex: instanceIndex,
            MonikerName: monikerName);
    }

    /// <summary>세션 정리. quitIfNew=true 면 우리가 띄운 경우만 한/글 종료.</summary>
    public static void Detach(HwpSession session, bool quitIfNew = false)
    {
        if (quitIfNew && session.IsNew)
        {
            try { session.Hwp.Quit(); }
            catch { /* 이미 종료된 상태일 수 있음 — 무시 */ }
        }
    }

    // ========================================================================
    // 내부 — ROT 순회, 레지스트리, spawn
    // ========================================================================

    private static HashSet<int> HwpProcessIds()
    {
        var pids = new HashSet<int>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (HwpProcessNames.Contains(p.ProcessName))
                    pids.Add(p.Id);
            }
            catch { /* AccessDenied 등 — skip */ }
            finally { p.Dispose(); }
        }
        return pids;
    }

    /// <summary>ROT 의 모든 moniker display name 집합 — 신규 spawn 인스턴스 식별용.</summary>
    private static HashSet<string> ListRotMonikers()
    {
        var names = new HashSet<string>();
        foreach (var (mk, displayName) in EnumerateRot())
        {
            names.Add(displayName);
            Marshal.ReleaseComObject(mk);
        }
        return names;
    }

    /// <summary>
    /// ROT 순회 — (moniker, display name) 페어. 호출자는 사용 후 ReleaseComObject 책임.
    /// </summary>
    private static IEnumerable<(IMoniker Moniker, string DisplayName)> EnumerateRot()
    {
        Ole32.CreateBindCtx(0, out var bindCtx);
        Ole32.GetRunningObjectTable(0, out var rot);
        try
        {
            rot.EnumRunning(out var enumMoniker);
            var monikers = new IMoniker[1];
            var fetched = IntPtr.Zero;
            while (enumMoniker.Next(1, monikers, fetched) == 0)
            {
                var mk = monikers[0];
                string name;
                try { mk.GetDisplayName(bindCtx, null!, out name); }
                catch { Marshal.ReleaseComObject(mk); continue; }
                yield return (mk, name);
            }
            Marshal.ReleaseComObject(enumMoniker);
        }
        finally
        {
            Marshal.ReleaseComObject(rot);
            Marshal.ReleaseComObject(bindCtx);
        }
    }

    /// <summary>
    /// ROT 에서 살아있는 HWPFrame.HwpObject 인스턴스를 모두 yield.
    /// dead moniker (ROT 에 잔존하지만 객체는 죽음) 는 자동 skip.
    /// </summary>
    private static IEnumerable<(dynamic Hwp, string MonikerName, int VersionCode, int InstanceIndex)>
        EnumerateLiveHwp()
    {
        Ole32.GetRunningObjectTable(0, out var rot);
        try
        {
            foreach (var (mk, name) in EnumerateRot())
            {
                var m = HwpObjectMonikerRegex.Match(name);
                if (!m.Success) { Marshal.ReleaseComObject(mk); continue; }

                dynamic? hwp = null;
                try
                {
                    rot.GetObject(mk, out var obj);
                    hwp = obj;
                }
                catch { /* 인스턴스 죽음 — skip */ }
                finally { Marshal.ReleaseComObject(mk); }

                if (hwp is null || !IsAlive(hwp)) continue;
                yield return (
                    hwp!,
                    name,
                    int.Parse(m.Groups[1].Value),
                    int.Parse(m.Groups[2].Value));
            }
        }
        finally { Marshal.ReleaseComObject(rot); }
    }

    private record FoundHwp(dynamic Hwp, string MonikerName, int VersionCode, int InstanceIndex);

    /// <summary>
    /// ROT 에서 살아있는 HWPFrame.HwpObject 인스턴스 첫 매칭 반환.
    /// preferMoniker 지정 시 그 moniker 와 일치하는 인스턴스만 (다른 인스턴스로
    /// silent fallback 금지 — 사용자가 의도하지 않은 한/글에 붙는 사고 방지).
    /// </summary>
    private static FoundHwp? FindInRot(string? preferMoniker)
    {
        foreach (var (hwp, name, ver, idx) in EnumerateLiveHwp())
        {
            if (preferMoniker is not null && name != preferMoniker) continue;
            return new FoundHwp(hwp, name, ver, idx);
        }
        return null;
    }

    /// <summary>
    /// 레지스트리에서 Hwp.exe 절대 경로 추출.
    ///
    /// ProgID HWPFrame.HwpObject → CLSID → LocalServer32 순으로 lookup.
    /// LocalServer32 값은 `"C:\\...\\Hwp.exe" /Automation` 같은 형식이므로
    /// 실행 파일 경로만 잘라 반환.
    /// </summary>
    private static string? FindHwpExe()
    {
        string? clsid;
        using (var k = Registry.ClassesRoot.OpenSubKey(@"HWPFrame.HwpObject\CLSID"))
            clsid = k?.GetValue("") as string;
        if (string.IsNullOrEmpty(clsid)) return null;

        var candidates = new (RegistryKey Hive, string Path)[]
        {
            (Registry.ClassesRoot,  $@"CLSID\{clsid}\LocalServer32"),
            (Registry.LocalMachine, $@"SOFTWARE\Classes\CLSID\{clsid}\LocalServer32"),
            (Registry.LocalMachine, $@"SOFTWARE\Classes\WOW6432Node\CLSID\{clsid}\LocalServer32"),
        };
        foreach (var (hive, path) in candidates)
        {
            using var k = hive.OpenSubKey(path);
            if (k?.GetValue("") is not string cmd || string.IsNullOrWhiteSpace(cmd))
                continue;
            cmd = cmd.Trim();
            // `"C:\...\Hwp.exe" /Automation` 또는 `C:\...\Hwp.exe /Automation`
            if (cmd.StartsWith('"'))
            {
                var end = cmd.IndexOf('"', 1);
                if (end > 0) return cmd[1..end];
            }
            // 따옴표 없음 — 경로에 공백(`Program Files`)이 있을 수 있어 split 금지.
            // .exe 까지 ungreedy 매칭.
            var m = Regex.Match(cmd, @"(.+?\.exe)(?:\s|$)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
            return cmd; // 최후 fallback
        }
        return null;
    }

    /// <summary>
    /// ShellExecute 로 Hwp.exe 실행 후 ROT 등록 대기 → attach.
    ///
    /// ★ Fasoo 등 사내 DRM 회피용. CoCreateInstance 로 띄우면 부모가 Forge.exe
    /// 라 DRM 이 자격증명 inject 안 함 → 생성 문서 권한 문제 발생.
    /// ShellExecute 경유는 사용자 직접 실행과 동등 처리됨.
    /// </summary>
    private static FoundHwp? SpawnViaShell(TimeSpan? timeout = null)
    {
        var exe = FindHwpExe();
        if (exe is null) return null;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch { return null; }

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(500);
            var found = FindInRot(preferMoniker: null);
            if (found is not null) return found;
        }
        return null;
    }
}

// ============================================================================
// ole32.dll P/Invoke — IRunningObjectTable / IBindCtx 진입점
// ============================================================================

internal static class Ole32
{
    [DllImport("ole32.dll", PreserveSig = false)]
    public static extern void CreateBindCtx(int reserved, out IBindCtx ppbc);

    [DllImport("ole32.dll", PreserveSig = false)]
    public static extern void GetRunningObjectTable(int reserved, out IRunningObjectTable prot);
}
