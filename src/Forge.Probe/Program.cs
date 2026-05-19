// PoC + 영구 진단 도구.
//
// 사용:
//   dotnet run --project src/Forge.Probe
//   dotnet run --project src/Forge.Probe -- list                 # ROT 인스턴스 나열
//   dotnet run --project src/Forge.Probe -- insert               # 첫 인스턴스에 텍스트 1줄 삽입
//   dotnet run --project src/Forge.Probe -- convert <in.md> <out.hwpx>  # md → 새 hwpx 변환
//
// 인자 없으면 list 동작.

using System.Reflection;
using Forge.Core;
using Forge.Core.Formatter;
using Forge.Core.Templates;

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
            "list"    => ListInstances(),
            "insert"  => InsertOneLine(),
            "convert" => ConvertMarkdown(args),
            "diag"    => DiagnoseDispatch(),
            _         => PrintUsage(),
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[probe] 실패: {ex.GetType().Name}: {ex.Message}");
        if (ex.InnerException is { } inner)
            Console.Error.WriteLine($"  caused by: {inner.GetType().Name}: {inner.Message}");
        // 진단 — 디버깅 중에는 stack 까지 (라인 번호 포함). 운영 시점에 제거 검토.
        Console.Error.WriteLine("  stack:");
        Console.Error.WriteLine(ex.StackTrace);
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

static int ConvertMarkdown(string[] args)
{
    // 사용: convert <input.md> [<output.hwpx>]
    // output 생략 시 input 폴더에 <stem>.hwpx 저장.
    if (args.Length < 2)
    {
        Console.Error.WriteLine("[probe] usage: convert <input.md> [<output.hwpx>]");
        return 64;
    }
    var inPath = Path.GetFullPath(args[1]);
    if (!File.Exists(inPath))
    {
        Console.Error.WriteLine($"[probe] 입력 md 파일 없음: {inPath}");
        return 66;
    }
    var outPath = args.Length >= 3
        ? Path.GetFullPath(args[2])
        : Path.ChangeExtension(inPath, ".hwpx");

    Console.WriteLine($"[probe] 입력 md  : {inPath}");
    Console.WriteLine($"[probe] 출력 hwpx: {outPath}");

    // 1. 파일 읽기 + 파싱 (COM 무관, 빠르게 검증)
    var src = File.ReadAllText(inPath);
    var doc = Parser.Parse(src);
    Console.WriteLine($"[probe] 파싱: {doc.Nodes.Count} 노드 (메타 보고서명={doc.Metadata.ReportTitle ?? "(없음)"})");

    // 2. 한/글 attach (W1 와 동일 정책 — 사용자가 먼저 띄운 상태)
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
        Console.Error.WriteLine($"[probe] {ex.Message}");
        return 3;
    }
    Console.WriteLine($"[probe] attach: {session.VersionName} #{session.InstanceIndex}");

    // 3. 변환 + 저장
    // session.Hwp 가 dynamic 이라 호출이 dynamic dispatch 가 됨 — 람다는 명시
    // delegate 변수로 받아야 함 (CS1977 회피).
    HwpxWriter.LogFn logFn = msg => Console.WriteLine($"  {msg}");
    HwpxWriter.GenerateHwpxViaCom(
        session.Hwp, doc,
        outPath: outPath,
        spec: ReportSpec.Report1,
        log: logFn,
        mode: HwpxWriteMode.New);

    Console.WriteLine($"[probe] ✔ 완료 → {outPath}");
    return 0;
}

static int DiagnoseDispatch()
{
    // 한컴 IDispatch.GetTypeInfo + PIA reflection 진단.
    // 표 생성 dispatch 가 막힐 때 어디까지 가능한지 확인.

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

    dynamic hwp = session.Hwp;
    hwp.HAction.GetDefault("TableCreate", hwp.HParameterSet.HTableCreation.HSet);
    object T = hwp.HParameterSet.HTableCreation;

    Console.WriteLine($"[diag] T (HParameterSet.HTableCreation) type: {T.GetType().FullName}");

    bool hasTypeInfo = TypelibDispatch.HasTypeInfo(T);
    Console.WriteLine($"[diag] T.HasTypeInfo: {hasTypeInfo}");

    if (hasTypeInfo)
    {
        try
        {
            int d1 = TypelibDispatch.FindDispId(T, "CreateItemArray");
            Console.WriteLine($"[diag] CreateItemArray dispid (via ITypeInfo): {d1}");
        }
        catch (Exception ex) { Console.WriteLine($"[diag] ITypeInfo FindDispId 실패: {ex.Message}"); }
    }

    // PIA reflection 으로 dispid
    var piaSetType = typeof(Forge.Interop.HwpObject.IDHwpParameterSet);
    var piaArrType = typeof(Forge.Interop.HwpObject.IDHwpParameterArray);
    Console.WriteLine($"[diag] PIA IDHwpParameterSet GUID: {piaSetType.GUID}");
    Console.WriteLine($"[diag] PIA IDHwpParameterArray GUID: {piaArrType.GUID}");

    foreach (var m in piaSetType.GetMethods().Where(m => m.Name is "CreateItemArray" or "Item"))
    {
        var d = m.GetCustomAttribute<System.Runtime.InteropServices.DispIdAttribute>()?.Value;
        Console.WriteLine($"[diag] PIA IDHwpParameterSet.{m.Name}: dispid={d}");
    }
    foreach (var m in piaArrType.GetMethods().Where(m => m.Name is "SetItem" or "Item"))
    {
        var d = m.GetCustomAttribute<System.Runtime.InteropServices.DispIdAttribute>()?.Value;
        Console.WriteLine($"[diag] PIA IDHwpParameterArray.{m.Name}: dispid={d}");
    }

    // ParameterArray 진단 — CreateItemArray 후 T.ColWidth 의 ITypeInfo dump
    Console.WriteLine($"[diag] --- ParameterArray 진단 ---");
    try
    {
        Forge.Core.TypelibDispatch.InvokeMethodViaTypeInfo(T, "CreateItemArray", "ColWidth", 1);
        var pa = Forge.Core.TypelibDispatch.GetPropertyViaTypeInfo(T, "ColWidth");
        Console.WriteLine($"[diag] T.ColWidth type: {pa?.GetType().FullName ?? "null"}");
        if (pa is not null)
        {
            Console.WriteLine($"[diag] T.ColWidth HasTypeInfo: {Forge.Core.TypelibDispatch.HasTypeInfo(pa)}");
            var paDump = Forge.Core.TypelibDispatch.DumpTypeInfoMembers(pa);
            foreach (var (name, dispid) in paDump.OrderBy(kv => kv.Value))
                Console.WriteLine($"[diag]   PA.{name,-25} dispid={dispid}");
        }
    }
    catch (Exception ex) { Console.WriteLine($"[diag] ParameterArray 진단 실패: {ex.Message}"); }

    // ITypeInfo.GetFuncDesc + GetNames 으로 typelib 의 정확한 dispid 추출 시도
    Console.WriteLine($"[diag] --- ITypeInfo dump (T = HTableCreation) ---");
    var typelibDump = Forge.Core.TypelibDispatch.DumpTypeInfoMembers(T);
    if (typelibDump.Count == 0)
    {
        Console.WriteLine("[diag] ITypeInfo dump 비어있음 — typelib 메서드 다 NotImplementedException");
    }
    else
    {
        Console.WriteLine($"[diag] {typelibDump.Count} 멤버 발견");
        foreach (var (name, dispid) in typelibDump.OrderBy(kv => kv.Value))
            Console.WriteLine($"[diag]   {name,-30} dispid={dispid}");
    }

    // 전체 PIA 멤버의 dispid vs 실제 IDispatch.GetIDsOfNames 응답 비교 — offset 패턴 확인
    Console.WriteLine($"[diag] --- PIA dispid vs 실제 IDispatch.GetIDsOfNames ---");
    var allMembers = new List<(string name, int? piaDispId)>();
    foreach (var p in piaSetType.GetProperties())
    {
        var d = p.GetCustomAttribute<System.Runtime.InteropServices.DispIdAttribute>()?.Value;
        if (d.HasValue) allMembers.Add((p.Name, d.Value));
    }
    foreach (var m in piaSetType.GetMethods().Where(m => !m.IsSpecialName))
    {
        var d = m.GetCustomAttribute<System.Runtime.InteropServices.DispIdAttribute>()?.Value;
        if (d.HasValue) allMembers.Add((m.Name, d.Value));
    }
    foreach (var (name, piaDispId) in allMembers.OrderBy(t => t.piaDispId).Take(20))
    {
        try
        {
            var realDispid = Forge.Core.TypelibDispatch.GetDispIdViaIDispatch(T, name);
            var diff = realDispid - piaDispId;
            Console.WriteLine($"[diag] {name,-20} PIA={piaDispId} actual={realDispid} diff={diff}");
        }
        catch
        {
            Console.WriteLine($"[diag] {name,-20} PIA={piaDispId} actual=N/A (GetIDsOfNames 실패)");
        }
    }

    return 0;
}

static int PrintUsage()
{
    Console.WriteLine("Forge.Probe — PoC + 진단 도구");
    Console.WriteLine("사용: Forge.Probe [list|insert|convert ...]");
    Console.WriteLine("  list                            — ROT 한/글 인스턴스 나열 (기본)");
    Console.WriteLine("  insert                          — 첫 인스턴스에 텍스트 1줄 삽입");
    Console.WriteLine("  convert <in.md> [<out.hwpx>]    — md → 새 hwpx (W2 검증)");
    return 0;
}
