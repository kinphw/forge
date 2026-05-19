// W1 PoC + 영구 진단 도구.
//
// 사용:
//   dotnet run --project src/Forge.Probe
//   dotnet run --project src/Forge.Probe -- list      # ROT 의 한/글 인스턴스 나열
//   dotnet run --project src/Forge.Probe -- insert    # 첫 인스턴스에 텍스트 1줄 삽입
//
// 인자 없으면 list 동작.

using Forge.Core;

// 한/글 COM 은 STA 필수 (apartment 정책). 콘솔 앱 Main 은 기본 MTA 라 명시.
// .NET 8 의 console 템플릿은 top-level statements 라 [STAThread] attribute 를
// 못 붙임 → 메인 스레드 대신 STA 스레드 1개 만들어 거기서 실행.

var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
int exitCode = 0;

var staThread = new Thread(() =>
{
    try
    {
        exitCode = cmd switch
        {
            "list"   => ListInstances(),
            "insert" => InsertOneLine(),
            _        => PrintUsage(),
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[probe] 실패: {ex.GetType().Name}: {ex.Message}");
        if (ex.InnerException is { } inner)
            Console.Error.WriteLine($"  caused by: {inner.GetType().Name}: {inner.Message}");
        exitCode = 1;
    }
});
staThread.SetApartmentState(ApartmentState.STA);
staThread.Start();
staThread.Join();

return exitCode;

static int ListInstances()
{
    var instances = HwpSessionHelpers.ListInstances();
    if (instances.Count == 0)
    {
        Console.WriteLine("[probe] ROT 에 등록된 한/글 인스턴스 없음.");
        Console.WriteLine("        한/글을 먼저 실행한 뒤 다시 시도해주세요.");
        return 0;
    }
    Console.WriteLine($"[probe] ROT 에서 발견된 한/글 인스턴스 {instances.Count}개:");
    foreach (var inst in instances)
    {
        Console.WriteLine($"  - {inst.DisplayLabel}");
        Console.WriteLine($"      moniker: {inst.MonikerName}");
    }
    return 0;
}

static int InsertOneLine()
{
    Console.WriteLine("[probe] AttachOrCreate(allowSpawn=false) 시도 ...");
    HwpSession session;
    try
    {
        session = HwpSessionHelpers.AttachOrCreate(visible: true, allowSpawn: false);
    }
    catch (NoExistingHwpException ex)
    {
        Console.Error.WriteLine($"[probe] {ex.Message}");
        return 2;
    }
    catch (MultipleHwpInstancesException ex)
    {
        // PoC 단계 — 다중 인스턴스면 사용자 명시 선택 요청. 자동 첫 매칭 금지.
        Console.Error.WriteLine($"[probe] {ex.Message}");
        Console.Error.WriteLine($"        후보 {ex.Instances.Count}개:");
        foreach (var inst in ex.Instances)
            Console.Error.WriteLine($"          - {inst.DisplayLabel}");
        return 3;
    }

    Console.WriteLine($"[probe] attach 성공: {session.VersionName} #{session.InstanceIndex}");
    Console.WriteLine($"        moniker: {session.MonikerName}");
    Console.WriteLine($"        is_new={session.IsNew}, pre_existing={session.PreExisting}");

    var text = $"[Forge probe] C# 포팅 W1 PoC — {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
    Console.WriteLine($"[probe] 텍스트 삽입: {text}");
    ComHelpers.InsertText(session.Hwp, text);
    ComHelpers.Run(session.Hwp, "BreakPara");
    Console.WriteLine("[probe] 완료 — 한/글 활성 문서 확인.");
    return 0;
}

static int PrintUsage()
{
    Console.WriteLine("Forge.Probe — W1 PoC + 진단 도구");
    Console.WriteLine("사용: Forge.Probe [list|insert]");
    Console.WriteLine("  list   — ROT 에 등록된 한/글 인스턴스 나열 (기본)");
    Console.WriteLine("  insert — 첫 인스턴스에 텍스트 1줄 삽입");
    return 0;
}
