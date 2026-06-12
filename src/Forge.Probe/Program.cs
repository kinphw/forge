// PoC + 영구 진단 도구.
//
// 사용:
//   dotnet run --project src/Forge.Probe
//   dotnet run --project src/Forge.Probe -- list                 # ROT 인스턴스 나열
//   dotnet run --project src/Forge.Probe -- insert               # 첫 인스턴스에 텍스트 1줄 삽입
//   dotnet run --project src/Forge.Probe -- convert <in.md> <out.hwpx>  # md → 새 hwpx 변환
//   dotnet run --project src/Forge.Probe -- font <fontName> [<sizePt>]  # SetFont 진단 (캐럿/선택영역)
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
            "scan"    => DiagnoseSelectionScan(),
            "mdconv"  => RunMdConvertSelection(),
            "font"    => DiagnoseSetFont(args),
            "font-routed" => DiagnoseSetFontRouted(args),
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

// ─────────────────────────────────────────────────────────────────────────
// 선택 영역 GetText state 시퀀스 덤프 — 비파괴적 (Delete 안 함).
// 표가 든 selection 이 실제로 어떤 GetText state 를 내는지 확인용.
//   state: 2 일반텍스트 / 3 다음문단 / 4 제어문자(표·개체) 진입 / 5 탈출 / 0·1 종료
// ─────────────────────────────────────────────────────────────────────────
static int DiagnoseSelectionScan()
{
    HwpSession session;
    try { session = HwpSessionHelpers.AttachOrCreate(visible: true, allowSpawn: false); }
    catch (NoExistingHwpException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 2; }
    catch (MultipleHwpInstancesException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 3; }
    Console.WriteLine($"[scan] attach: {session.VersionName} #{session.InstanceIndex}");

    dynamic hwp = session.Hwp;

    object hwpObj = hwp;
    var sel = Forge.Core.Linter.Range.SelectionRange(hwpObj);
    Console.WriteLine($"[scan] SelectionRange = {(sel.HasValue ? $"{sel.Value.Start} → {sel.Value.End}" : "null (단일 캐럿/거짓음성)")}");

    Console.WriteLine("[scan] --- InitScan(null, 0xff) + GetText 루프 (끝까지, break 안 함) ---");
    if ((object)hwp is Forge.Interop.HwpObject.IHwpObject typed)
    {
        typed.InitScan(null, 0xff, null, null, null, null);
        try
        {
            for (int i = 0; i < 500; i++)
            {
                int state = typed.GetText(out string chunk);
                string preview = (chunk ?? "")
                    .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
                if (preview.Length > 70) preview = preview.Substring(0, 70) + "…";
                Console.WriteLine($"[scan] #{i,-3} state={state} len={chunk?.Length ?? 0} \"{preview}\"");
                // 0/1/101/102 = 스캔 종료 — 그 외(2/3/4/5)는 계속 진행해 표가 무슨 state 인지 관찰.
                if (state is 0 or 1 or 101 or 102)
                {
                    Console.WriteLine($"[scan] === 스캔 종료 (state={state}) ===");
                    break;
                }
            }
        }
        finally { try { typed.ReleaseScan(); } catch { } }
    }
    else
    {
        Console.WriteLine("[scan] ✘ PIA cast 실패 — IHwpObject 아님 (GetTextFile fallback 경로)");
    }

    Console.WriteLine("[scan] --- HeadCtrl 순회 (문서 전체 컨트롤 + 앵커) ---");
    try
    {
#pragma warning disable CS8602
        dynamic ctrl = hwp.HeadCtrl;
        int ci = 0;
        while (ctrl != null && ci < 2000)
        {
            string ctrlId;
            try { ctrlId = (string)ctrl.CtrlID; } catch { ctrlId = "(id?)"; }
            int ctrlCh;
            try { ctrlCh = (int)ctrl.CtrlCh; } catch { ctrlCh = -1; }
            string anchorStr;
            try
            {
                dynamic ap = ctrl.GetAnchorPos(0);
                anchorStr = ap == null
                    ? "null"
                    : $"({(int)ap.Item("List")},{(int)ap.Item("Para")},{(int)ap.Item("Pos")})";
            }
            catch (Exception e) { anchorStr = $"err:{e.Message}"; }
            Console.WriteLine($"[scan]   ctrl#{ci} id='{ctrlId}' ch={ctrlCh} anchor={anchorStr}");
            ctrl = ctrl.Next;
            ci++;
        }
#pragma warning restore CS8602
        Console.WriteLine($"[scan]   총 {ci} 컨트롤");
    }
    catch (Exception e) { Console.WriteLine($"[scan]   HeadCtrl 순회 실패: {e.GetType().Name}: {e.Message}"); }

    if (sel.HasValue)
    {
        bool hasObj = Forge.Core.Linter.Range.SelectionContainsInlineObject(hwp, sel.Value.Start, sel.Value.End);
        Console.WriteLine($"[scan] >>> SelectionContainsInlineObject = {hasObj} (true 면 변환 거부 대상) <<<");
    }

    Console.WriteLine("[scan] --- 현재 Primitives.GetSelectionText 결과 (수정본) ---");
    string raw = Forge.Core.Renderers.Primitives.GetSelectionText(hwpObj, out bool containsObject);
    Console.WriteLine($"[scan] len={raw.Length} containsObject={containsObject}");
    Console.WriteLine($"[scan] 추출 텍스트 ↓↓↓\n{raw}\n[scan] ↑↑↑ 끝");

    return 0;
}

// ─────────────────────────────────────────────────────────────────────────
// 선택 영역 md 변환 실측 — Ctrl+Shift+X 와 동일 경로. 변환 전후 표(tbl) 컨트롤
// 개수를 세어 표 보존 여부를 정량 검증.
// ─────────────────────────────────────────────────────────────────────────
static int RunMdConvertSelection()
{
    HwpSession session;
    try { session = HwpSessionHelpers.AttachOrCreate(visible: true, allowSpawn: false); }
    catch (NoExistingHwpException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 2; }
    catch (MultipleHwpInstancesException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 3; }
    Console.WriteLine($"[mdconv] attach: {session.VersionName} #{session.InstanceIndex}");
    dynamic hwp = session.Hwp;

    int before = CountTables(hwp);
    Console.WriteLine($"[mdconv] 변환 전 표(tbl) 컨트롤: {before}");

    try
    {
        Forge.Core.Formatter.HwpxWriter.LogFn lg = msg => Console.WriteLine($"  {msg}");
        int n = Forge.Core.Formatter.HwpxWriter.ConvertSelectionToHwpx(hwp, ReportSpec.Report1, lg);
        Console.WriteLine($"[mdconv] 변환 결과: {n} 노드");
    }
    catch (Forge.Core.Formatter.NoSelectionException ex)
    {
        Console.WriteLine($"[mdconv] 변환 거부(NoSelectionException): {ex.Message}");
    }

    int after = CountTables(hwp);
    string verdict = after >= before ? "✔ 표 보존 OK" : $"✘ 표 손실! ({before} → {after})";
    Console.WriteLine($"[mdconv] 변환 후 표(tbl) 컨트롤: {after}  → {verdict}");
    return 0;
}

// HeadCtrl ~ Next 순회로 표(CtrlID=="tbl") 컨트롤 개수 카운트.
static int CountTables(dynamic hwp)
{
    int count = 0;
    try
    {
#pragma warning disable CS8602
        dynamic ctrl = hwp.HeadCtrl;
        int guard = 0;
        while (ctrl != null && guard < 100000)
        {
            try { if ((string)ctrl.CtrlID == "tbl") count++; } catch { }
            ctrl = ctrl.Next;
            guard++;
        }
#pragma warning restore CS8602
    }
    catch { }
    return count;
}

static int PrintUsage()
{
    Console.WriteLine("Forge.Probe — PoC + 진단 도구");
    Console.WriteLine("사용: Forge.Probe [list|insert|convert ...]");
    Console.WriteLine("  list                            — ROT 한/글 인스턴스 나열 (기본)");
    Console.WriteLine("  insert                          — 첫 인스턴스에 텍스트 1줄 삽입");
    Console.WriteLine("  convert <in.md> [<out.hwpx>]    — md → 새 hwpx (W2 검증)");
    Console.WriteLine("  scan                            — 현재 selection 의 컨트롤·텍스트 덤프 (비파괴)");
    Console.WriteLine("  mdconv                          — 현재 selection md 변환 + 표 보존 검증");
    Console.WriteLine("  font <fontName> [<sizePt>]      — raw 7면 TTF set 스모크테스트");
    Console.WriteLine("  font-routed <fontName> [<sizePt>]");
    Console.WriteLine("                                  — Primitives.SetFont 라우팅 호출 (휴먼명조 → HFT dispatch)");
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────
// SetFont 스모크테스트
//
// 사용: probe font "HY헤드라인M" 15
//   1. ROT 의 첫 한/글 인스턴스에 attach
//   2. 캐럿/선택영역에 SetFont 와 동치인 5단계 풀어쓰기 수행 (Execute 반환값 캡처)
//   3. 적용 후 CharShape GetDefault 로 7면 face/type readback
//   4. "비교용 텍스트" 1줄 insert → 한/글 화면에서 폰트 확인 가능
//
// 출력 가이드:
//   - Execute 반환값 == False → ParameterSet 자체 거부 (보통 잘못된 키/값)
//   - Execute == True but readback face != 요청 face → 한컴이 시스템에서 face 못 찾고 fallback
//   - Execute == True + readback face == 요청 face → 적용 성공, 시각 미반영 시 다른 곳 의심
// ─────────────────────────────────────────────────────────────────────────
static int DiagnoseSetFont(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("[probe] usage: font <fontName> [<sizePt>]");
        Console.Error.WriteLine("  예: probe font \"HY헤드라인M\" 15");
        return 64;
    }
    var fontName = args[1];
    var sizePt = args.Length >= 3 && double.TryParse(args[2], out var p) ? p : 15.0;

    Console.WriteLine($"[font] 요청: face='{fontName}' size={sizePt}pt");
    Console.WriteLine($"[font] face codepoints:");
    for (int i = 0; i < fontName.Length; i++)
        Console.WriteLine($"         [{i}] '{fontName[i]}' U+{(int)fontName[i]:X4}");

    HwpSession session;
    try
    {
        session = HwpSessionHelpers.AttachOrCreate(visible: true, allowSpawn: false);
    }
    catch (NoExistingHwpException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 2; }
    catch (MultipleHwpInstancesException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 3; }
    Console.WriteLine($"[font] attach: {session.VersionName} #{session.InstanceIndex}");
    dynamic hwp = session.Hwp;

    // ── 1) 5단계 풀어쓰기 — Execute 반환값 캡처 (face/type)
    Console.WriteLine($"[font] (1/4) face/type 7면 set + Execute …");
    var act = hwp.CreateAction("CharShape");
    var s = act.CreateSet();
    act.GetDefault(s);
    var faceItems = new (string K, object V)[]
    {
        ("FaceNameUser",     fontName), ("FontTypeUser",     1),
        ("FaceNameSymbol",   fontName), ("FontTypeSymbol",   1),
        ("FaceNameOther",    fontName), ("FontTypeOther",    1),
        ("FaceNameJapanese", fontName), ("FontTypeJapanese", 1),
        ("FaceNameHanja",    fontName), ("FontTypeHanja",    1),
        ("FaceNameLatin",    fontName), ("FontTypeLatin",    1),
        ("FaceNameHangul",   fontName), ("FontTypeHangul",   1),
    };
    foreach (var (k, v) in faceItems)
    {
        try { s.SetItem(k, v); }
        catch (Exception ex) { Console.WriteLine($"         ✘ SetItem {k} = {v} 실패: {ex.Message}"); }
    }
    object execResult = act.Execute(s);
    Console.WriteLine($"         Execute 반환: {execResult} ({execResult?.GetType().FullName})");

    // ── 2) Height 별도 Execute
    Console.WriteLine($"[font] (2/4) Height 별도 set + Execute …");
    var act2 = hwp.CreateAction("CharShape");
    var s2 = act2.CreateSet();
    act2.GetDefault(s2);
    int hwpHeight = (int)(sizePt * 100);
    try { s2.SetItem("Height", hwpHeight); }
    catch (Exception ex) { Console.WriteLine($"         ✘ SetItem Height = {hwpHeight} 실패: {ex.Message}"); }
    object execResult2 = act2.Execute(s2);
    Console.WriteLine($"         Execute 반환: {execResult2}");

    // ── 3) Readback — 현재 typing attr 의 7면 face/type
    Console.WriteLine($"[font] (3/4) Readback (CharShape GetDefault) …");
    try
    {
        var rb = hwp.HParameterSet.HCharShape;
        hwp.HAction.GetDefault("CharShape", rb.HSet);
        string[] faces = { "Hangul", "Latin", "User", "Symbol", "Other", "Japanese", "Hanja" };
        foreach (var f in faces)
        {
            object face = ((object)rb).GetType().InvokeMember(
                $"FaceName{f}", System.Reflection.BindingFlags.GetProperty, null, rb, null) ?? "";
            object type = ((object)rb).GetType().InvokeMember(
                $"FontType{f}", System.Reflection.BindingFlags.GetProperty, null, rb, null) ?? 0;
            var faceStr = face?.ToString() ?? "";
            var ok = faceStr == fontName ? "✔" : "✘";
            Console.WriteLine($"         {ok} {f,-9} face='{faceStr}' type={type}");
        }
        object height = ((object)rb).GetType().InvokeMember(
            "Height", System.Reflection.BindingFlags.GetProperty, null, rb, null) ?? 0;
        Console.WriteLine($"         Height = {height} (요청 {hwpHeight})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"         readback 실패: {ex.Message}");
    }

    // ── 4) 비교 텍스트 insert — 한/글 화면 시각 확인
    Console.WriteLine($"[font] (4/4) 비교 텍스트 insert …");
    try
    {
        ComHelpers.InsertText(hwp, $"[{fontName} {sizePt}pt] 한글ABC 가나다 0123");
        ComHelpers.Run(hwp, "BreakPara");
        Console.WriteLine($"         ✔ 한/글 화면 확인 — 위 줄의 face 가 '{fontName}' 인지 봐주세요.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"         insert 실패: {ex.Message}");
    }

    return 0;
}

// ─────────────────────────────────────────────────────────────────────────
// SetFont 라우팅 스모크테스트 — Primitives.SetFont 직접 호출.
// "휴먼명조" 면 내부적으로 SetFontHumanmyongjo (FontType=2 HFT + 7면 별 face) 로 dispatch.
// 그 외엔 일반 SetFont (FontType=1 TTF + 7면 동일 face).
//
// readback 으로 7면 face/type 확인 — HFT 라우팅이 실제로 통하는지 검증.
// ─────────────────────────────────────────────────────────────────────────
static int DiagnoseSetFontRouted(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("[probe] usage: font-routed <fontName> [<sizePt>]");
        return 64;
    }
    var fontName = args[1];
    var sizePt = args.Length >= 3 && double.TryParse(args[2], out var p) ? p : 15.0;

    Console.WriteLine($"[font-routed] 요청: face='{fontName}' size={sizePt}pt");
    Console.WriteLine($"[font-routed] 라우팅: {(fontName == "휴먼명조" ? "SetFontHumanmyongjo (FontType=2 HFT)" : "SetFont 일반 (FontType=1 TTF)")}");

    HwpSession session;
    try
    {
        session = HwpSessionHelpers.AttachOrCreate(visible: true, allowSpawn: false);
    }
    catch (NoExistingHwpException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 2; }
    catch (MultipleHwpInstancesException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 3; }
    Console.WriteLine($"[font-routed] attach: {session.VersionName} #{session.InstanceIndex}");
    dynamic hwp = session.Hwp;

    Console.WriteLine($"[font-routed] (1/3) Primitives.SetFont 호출 …");
    try
    {
        Forge.Core.Renderers.Primitives.SetFont(hwp, fontName, sizePt, bold: false);
        Console.WriteLine($"         ✔ 예외 없이 반환 (Execute 반환값은 SetParam 내부에서 무시됨)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"         ✘ 예외: {ex.GetType().Name}: {ex.Message}");
    }

    Console.WriteLine($"[font-routed] (2/3) Readback (CharShape GetDefault) …");
    try
    {
        var rb = hwp.HParameterSet.HCharShape;
        hwp.HAction.GetDefault("CharShape", rb.HSet);
        string[] faces = { "Hangul", "Latin", "User", "Symbol", "Other", "Japanese", "Hanja" };

        // 휴먼명조 라우팅 시 7면 별 기대 face
        var expected = fontName == "휴먼명조"
            ? new Dictionary<string, (string face, int type)>
            {
                ["Hangul"]   = ("휴먼명조",   2),
                ["Latin"]    = ("HCI Poppy",  2),
                ["User"]     = ("명조",       2),
                ["Symbol"]   = ("한양신명조", 2),
                ["Other"]    = ("한양신명조", 2),
                ["Japanese"] = ("한양신명조", 2),
                ["Hanja"]    = ("한양신명조", 2),
            }
            : faces.ToDictionary(f => f, _ => (fontName, 1));

        foreach (var f in faces)
        {
            object face = ((object)rb).GetType().InvokeMember(
                $"FaceName{f}", System.Reflection.BindingFlags.GetProperty, null, rb, null) ?? "";
            object type = ((object)rb).GetType().InvokeMember(
                $"FontType{f}", System.Reflection.BindingFlags.GetProperty, null, rb, null) ?? 0;
            var faceStr = face?.ToString() ?? "";
            var typeInt = Convert.ToInt32(type);
            var exp = expected[f];
            var ok = (faceStr == exp.Item1 && typeInt == exp.Item2) ? "✔" : "✘";
            Console.WriteLine($"         {ok} {f,-9} face='{faceStr}' type={typeInt}  (기대: '{exp.Item1}' type={exp.Item2})");
        }
        object height = ((object)rb).GetType().InvokeMember(
            "Height", System.Reflection.BindingFlags.GetProperty, null, rb, null) ?? 0;
        var heightInt = Convert.ToInt32(height);
        int expectedHeight = (int)(sizePt * 100);
        Console.WriteLine($"         {(heightInt == expectedHeight ? "✔" : "✘")} Height = {heightInt} (요청 {expectedHeight})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"         readback 실패: {ex.Message}");
    }

    Console.WriteLine($"[font-routed] (3/3) 비교 텍스트 insert …");
    try
    {
        ComHelpers.InsertText(hwp, $"[routed:{fontName} {sizePt}pt] 한글ABC 가나다 0123");
        ComHelpers.Run(hwp, "BreakPara");
        Console.WriteLine($"         ✔ 한/글 화면 확인 — 위 줄의 face 가 '{fontName}' 인지.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"         insert 실패: {ex.Message}");
    }

    return 0;
}
