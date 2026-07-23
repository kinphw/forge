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
            "tabledump" => DiagnoseTableDump(),
            "cursortest" => DiagnoseCursorInsert(args),
            "glossary" => DiagnoseGlossaryState(),
            "gloss"    => DiagnoseGlossaryState(),
            "glossx"   => DiagnoseGlossaryExec(),
            "glosstest" => RunGlossaryTest(),
            "mdconv"  => RunMdConvertSelection(),
            "parse"   => ParseMarkdownFile(args),
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

    // 줄 단위 분해 — 줄바꿈 보존/형태 확인 (표 인식은 줄 분리에 의존).
    var splitLines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    Console.WriteLine($"[scan] 줄 수: {splitLines.Length}");
    for (int li = 0; li < splitLines.Length && li < 40; li++)
        Console.WriteLine($"[scan]   line[{li}] len={splitLines[li].Length} \"{splitLines[li]}\"");

    // 추출 텍스트를 그대로 Parser 에 통과 — X 변환과 동일 경로의 파싱 결과.
    var pdoc = Forge.Core.Formatter.Parser.Parse(raw.TrimEnd());
    Console.WriteLine($"[scan] Parser.Parse → {pdoc.Nodes.Count} 노드: " +
        string.Join(", ", pdoc.Nodes.Select(n => n.Type.ToString())));

    // 비공백 문자의 codepoint 덤프 — 네모숫자 등 HWP 기호의 실제 U+ 값 확인용.
    //   Rune 기반이라 보충문자(U+1F000+, surrogate pair)도 정확히 한 항목으로 출력.
    Console.WriteLine("[scan] 비공백 문자 codepoints:");
    foreach (var rune in raw.EnumerateRunes())
    {
        if (System.Text.Rune.IsWhiteSpace(rune)) continue;
        Console.WriteLine($"         '{rune}' U+{rune.Value:X4}");
    }

    return 0;
}

// ─────────────────────────────────────────────────────────────────────────
// 상용구(Ctrl+Shift+I) 진단 — 현재 캐럿/조합 상태를 비파괴로 덤프.
//   ㅁ 조합 중(blink) 상태에서 캐럿이 ㅁ 앞/뒤 어디로 잡히는지, 범위선택인지 확인.
//   먼저 이동 없는 read(caret/sel/selMode/현재 block) → 그다음 현재 문단 텍스트를
//   읽어 caret.Pos 기준 앞/뒤 글자를 코드포인트까지 확인 (캐럿은 마지막에 복원).
// ─────────────────────────────────────────────────────────────────────────
static int DiagnoseGlossaryState()
{
    HwpSession session;
    try { session = HwpSessionHelpers.AttachOrCreate(visible: true, allowSpawn: false); }
    catch (NoExistingHwpException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 2; }
    catch (MultipleHwpInstancesException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 3; }
    Console.WriteLine($"[gloss] attach: {session.VersionName} #{session.InstanceIndex}");

    dynamic hwp = session.Hwp;
    object hwpObj = hwp;

    // 1) 이동 없는 read (조합 상태 최대한 보존)
    var caret = Forge.Core.Linter.Range.GetCaretPos(hwpObj);
    Console.WriteLine($"[gloss] GetPosBySet caret = {caret}");

    var sel = Forge.Core.Linter.Range.SelectionRange(hwpObj);
    Console.WriteLine($"[gloss] SelectionRange = {(sel.HasValue ? $"{sel.Value.Start} → {sel.Value.End}" : "null (범위선택 아님)")}");

    try { int sm = (int)hwp.SelectionMode; Console.WriteLine($"[gloss] SelectionMode = {sm} (&0x0F = {sm & 0x0F})"); }
    catch (Exception e) { Console.WriteLine($"[gloss] SelectionMode 조회 실패: {e.Message}"); }

    bool piaOk = (object)hwp is Forge.Interop.HwpObject.IHwpObject;
    Console.WriteLine($"[gloss] PIA(IHwpObject) cast = {piaOk} (false 면 GetTextFile fallback 경로 사용)");

    // 2) 현재 문단 텍스트 + caret.Pos 로 앞/뒤 글자 판정 (캐럿 저장 후 복원)
    //    PIA 불가 환경이라 Primitives.GetSelectionText (fallback 포함) 로 읽는다.
    Console.WriteLine("[gloss] --- 현재 문단 스캔 (MoveParaBegin→MoveSelParaEnd) ---");
    hwp.Run("MoveParaBegin");
    hwp.Run("MoveSelParaEnd");
    string paraText = Forge.Core.Renderers.Primitives.GetSelectionText(hwpObj, out _);
    hwp.Run("Cancel");
    // 캐럿 복원
    try { Forge.Core.Linter.Range.SetCaretPos(hwpObj, caret); } catch { }

    Console.WriteLine($"[gloss] 문단 텍스트 = '{Vis(paraText)}' (len={paraText.Length})");
    Console.WriteLine($"[gloss] 문단 codepoints: {Cps(paraText)}");
    int pos = caret.Pos;
    Console.WriteLine($"[gloss] caret.Pos = {pos}  (문단 내 0-based 글자 위치)");
    string before = pos - 1 >= 0 && pos - 1 < paraText.Length ? paraText[pos - 1].ToString() : "(없음)";
    string after  = pos >= 0 && pos < paraText.Length ? paraText[pos].ToString() : "(없음)";
    Console.WriteLine($"[gloss] 캐럿 바로 앞 글자(Pos-1) = '{Vis(before)}' {Cps(before)}");
    Console.WriteLine($"[gloss] 캐럿 바로 뒤 글자(Pos)   = '{Vis(after)}' {Cps(after)}");
    Console.WriteLine("[gloss] → 상용구 준말(예 ㅁ)이 '앞'에 있으면 MoveSelPrevChar, '뒤'에 있으면 MoveSelNextChar 대상.");

    return 0;
}

// 조합 중(IME) 상태에서 "SetCaretPos 재배치 없이 바로 MoveSelPrevChar" 가
// 방금 친 글자를 선택하는지 실측 + 실제 치환까지 검증. (한 번의 ㅁ 재입력으로 확인)
static int DiagnoseGlossaryExec()
{
    HwpSession session;
    try { session = HwpSessionHelpers.AttachOrCreate(visible: true, allowSpawn: false); }
    catch (NoExistingHwpException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 2; }
    catch (MultipleHwpInstancesException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 3; }
    Console.WriteLine($"[glossx] attach: {session.VersionName} #{session.InstanceIndex}");

    dynamic hwp = session.Hwp;
    object hwpObj = hwp;

    var caret0 = Forge.Core.Linter.Range.GetCaretPos(hwpObj);
    Console.WriteLine($"[glossx] 조합 상태 GetPos = {caret0}");

    // 재배치 없이 바로 뒤로 1글자 선택 (첫 액션이 조합 확정 → 방금 친 글자 선택 가정)
    hwp.Run("MoveSelPrevChar");
    string sel1 = Forge.Core.Renderers.Primitives.GetSelectionText(hwpObj, out _);
    var caretAfterSel = Forge.Core.Linter.Range.GetCaretPos(hwpObj);
    Console.WriteLine($"[glossx] MoveSelPrevChar 후 선택='{Vis(sel1)}' {Cps(sel1)}  caret={caretAfterSel}");

    var entries = Forge.Core.Glossary.Load();
    bool converted = false;
    foreach (var e in entries)
    {
        if (sel1 == e.Before)
        {
            hwp.Run("Delete");
            ComHelpers.InsertText(hwp, e.After);
            Console.WriteLine($"[glossx] ✔ 치환 '{e.Before}' → '{e.After}'");
            converted = true;
            break;
        }
    }
    if (!converted)
    {
        hwp.Run("Cancel");
        Console.WriteLine($"[glossx] 매치 없음 (선택='{Vis(sel1)}') — 선택 해제만 함");
    }
    return 0;
}

// 확정 입력(조합 아님) 기준으로 GlossaryExpand 의 COM 메커니즘 검증.
// 문서 끝에 각 준말을 입력→확장하고 결과 문단을 읽어 본말로 바뀌는지 확인.
// (IME 조합 경로는 COM 으로 재현 불가 — 그건 실제 키보드 입력으로만 테스트 가능)
static int RunGlossaryTest()
{
    HwpSession session;
    try { session = HwpSessionHelpers.AttachOrCreate(visible: true, allowSpawn: false); }
    catch (NoExistingHwpException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 2; }
    catch (MultipleHwpInstancesException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 3; }
    Console.WriteLine($"[glosstest] attach: {session.VersionName} #{session.InstanceIndex}");

    dynamic hwp = session.Hwp;
    object hwpObj = hwp;
    var entries = Forge.Core.Glossary.Load();
    Console.WriteLine($"[glosstest] entries: {string.Join(", ", entries.Select(e => $"'{e.Before}'→'{e.After}'"))}");

    hwp.Run("MoveDocEnd");
    hwp.Run("BreakPara");
    ComHelpers.InsertText(hwp, "[Forge 상용구 자동테스트]");
    hwp.Run("BreakPara");

    Forge.Core.Linter.LogFn lg = m => Console.WriteLine($"      {m}");
    int pass = 0, fail = 0;
    foreach (var e in entries)
    {
        ComHelpers.InsertText(hwp, e.Before);   // 확정 입력 — 캐럿은 글자 뒤
        bool ret = Forge.Core.Linter.GlossaryExpand.ExpandAtCaret(hwp, entries, lg);

        hwp.Run("MoveParaBegin");
        hwp.Run("MoveSelParaEnd");
        string para = Forge.Core.Renderers.Primitives.GetSelectionText(hwpObj, out _);
        hwp.Run("Cancel");
        hwp.Run("MoveParaEnd");

        bool ok = para == e.After;
        if (ok) pass++; else fail++;
        Console.WriteLine($"[glosstest] {(ok ? "✔" : "✘")} '{e.Before}' → 문단='{Vis(para)}' {Cps(para)} (기대 '{e.After}', ret={ret})");
        hwp.Run("BreakPara");
    }

    // ── 시나리오 2: 캐럿이 '다음 문단 시작'(Pos 0)인 상태 — 확정 글자 + 문단 끝에서
    //    주입된 오른쪽 방향키가 캐럿을 다음 문단으로 넘긴 실사용 케이스 재현
    //    (사용자 실측 2026-07-23: 앞 len=1 선택='' → 매치 없음). MoveLeft 복귀 보정 검증.
    Console.WriteLine("[glosstest] ── 시나리오 2: 캐럿=다음 문단 시작 (방향키 문단 넘김 재현) ──");
    foreach (var e in entries)
    {
        ComHelpers.InsertText(hwp, e.Before);
        hwp.Run("BreakPara");   // 캐럿 → 다음(빈) 문단 시작 = 방향키가 넘긴 상태와 동일
        bool ret = Forge.Core.Linter.GlossaryExpand.ExpandAtCaret(hwp, entries, lg);

        // 보정이 맞았다면 이전 문단의 준말이 본말로 치환되고 캐럿은 그 문단에 있음.
        hwp.Run("MoveParaBegin");
        hwp.Run("MoveSelParaEnd");
        string para = Forge.Core.Renderers.Primitives.GetSelectionText(hwpObj, out _);
        hwp.Run("Cancel");

        bool ok = para == e.After;
        if (ok) pass++; else fail++;
        Console.WriteLine($"[glosstest] {(ok ? "✔" : "✘")} (문단넘김) '{e.Before}' → 문단='{Vis(para)}' (기대 '{e.After}', ret={ret})");
        hwp.Run("MoveDocEnd");
    }

    Console.WriteLine($"[glosstest] 결과: {pass} pass / {fail} fail (총 {entries.Count * 2})");
    Console.WriteLine("[glosstest] 한/글 문서 끝에 테스트 라인 확인 — Ctrl+Z 로 되돌리기 가능.");
    return fail == 0 ? 0 : 1;
}

// ─────────────────────────────────────────────────────────────────────────
// 표 셀 덤프 — 활성 문서의 모든 list(본문+셀)를 순회하며 셀 텍스트·폰트·배경색·
// 셀 폭/높이·4변 테두리를 읽어 콘솔에 출력. 실제 참조 hwp 의 박스 서식 추출용.
//   색: HWP RGBColor = COLORREF 0x00BBGGRR (r=low byte). mm = unit * 25.4 / 7200.
// ─────────────────────────────────────────────────────────────────────────
static int DiagnoseTableDump()
{
    HwpSession session;
    try { session = HwpSessionHelpers.AttachOrCreate(visible: true, allowSpawn: false); }
    catch (NoExistingHwpException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 2; }
    catch (MultipleHwpInstancesException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 3; }
    Console.WriteLine($"[tabledump] attach: {session.VersionName} #{session.InstanceIndex}");

    dynamic hwp = session.Hwp;
    object hwpObj = hwp;
    var origin = Forge.Core.Linter.Range.GetCaretPos(hwpObj);

    for (int listId = 0; listId <= 40; listId++)
    {
        try { Forge.Core.Linter.Range.SetCaretPos(hwpObj, new Forge.Core.Linter.CaretPos(listId, 0, 0)); }
        catch { continue; }
        var cur = Forge.Core.Linter.Range.GetCaretPos(hwpObj);
        if (cur.List != listId) continue;   // 도달 못한 list-id

        // 셀 텍스트
        string text;
        try
        {
            hwp.Run("MoveParaBegin");
            hwp.Run("MoveSelParaEnd");
            text = Forge.Core.Renderers.Primitives.GetSelectionText(hwpObj, out _);
            hwp.Run("Cancel");
        }
        catch { text = "(read fail)"; }
        string vis = Vis(text).Trim();

        // 셀 여부 판정 (CellShape — 표 밖이면 예외)
        bool inCell;
        try { var cs0 = hwp.CellShape; inCell = cs0 != null; }
        catch { inCell = false; }

        // 본문(list 0) 이면서 비어있으면 스킵 (셀만 관심)
        if (listId == 0 && !inCell) continue;

        Console.WriteLine($"[tabledump] === list #{listId} {(inCell ? "(셀)" : "(본문/기타)")} text='{vis}' ===");

        // 폰트 (CharShape readback — 캐럿 문단 선택)
        try
        {
            hwp.Run("MoveParaBegin");
            hwp.Run("MoveSelParaEnd");
            var rb = hwp.HParameterSet.HCharShape;
            hwp.HAction.GetDefault("CharShape", rb.HSet);
            object face   = ((object)rb).GetType().InvokeMember("FaceNameHangul", BindingFlags.GetProperty, null, rb, null) ?? "";
            object height = ((object)rb).GetType().InvokeMember("Height",         BindingFlags.GetProperty, null, rb, null) ?? 0;
            object bold   = ((object)rb).GetType().InvokeMember("Bold",           BindingFlags.GetProperty, null, rb, null) ?? 0;
            object tcol   = ((object)rb).GetType().InvokeMember("TextColor",      BindingFlags.GetProperty, null, rb, null) ?? 0;
            double pt = Convert.ToDouble(height) / 100.0;
            Console.WriteLine($"             font face='{face}' size={pt}pt bold={bold} textColor={DecColor(tcol)}");
            hwp.Run("Cancel");

            // 문단 정렬 (AlignType: 0=양쪽 1=왼쪽 2=오른쪽 3=가운데 4=배분 5=나눔) + 들여쓰기
            try
            {
                hwp.Run("MoveParaBegin");
                var ps = hwp.HParameterSet.HParaShape;
                hwp.HAction.GetDefault("ParagraphShape", ps.HSet);
                object al = ((object)ps).GetType().InvokeMember("AlignType", BindingFlags.GetProperty, null, ps, null) ?? -1;
                object ind = ((object)ps).GetType().InvokeMember("Indentation", BindingFlags.GetProperty, null, ps, null) ?? 0;
                string alName = Convert.ToInt32(al) switch { 0=>"양쪽",1=>"왼쪽",2=>"오른쪽",3=>"가운데",4=>"배분",5=>"나눔",_=>"?" };
                Console.WriteLine($"             para AlignType={al}({alName}) Indent={ind}");
            }
            catch (Exception e) { Console.WriteLine($"             para read fail: {e.Message}"); }
        }
        catch (Exception e) { Console.WriteLine($"             font read fail: {e.Message}"); }

        if (!inCell) continue;

        // ★ 셀 배경/테두리/폭은 캐럿만으론 GetDefault 가 반영 안 함 — 셀 블록 선택 후 읽는다.
        //   반복 간 selection 잔재 제거 위해 Cancel + 캐럿 재설정 후 TableCellBlock.
        try
        {
            hwp.Run("Cancel");
            Forge.Core.Linter.Range.SetCaretPos(hwpObj, new Forge.Core.Linter.CaretPos(listId, 0, 0));
            hwp.Run("TableCellBlock");
        }
        catch { }
        try { Console.WriteLine($"             SelectionMode(셀블록됨?)={(int)hwp.SelectionMode}"); } catch { }

        // 셀 폭/높이 (셀 블록 상태 CellShape)
        try
        {
            var cs = hwp.CellShape;
            object? w = null, h = null;
            try { w = cs.Item("Width"); } catch { }
            try { h = cs.Item("Height"); } catch { }
            double wmm = w != null ? Convert.ToDouble(w) * 25.4 / 7200.0 : -1;
            double hmm = h != null ? Convert.ToDouble(h) * 25.4 / 7200.0 : -1;
            Console.WriteLine($"             cell W={wmm:F2}mm H={hmm:F2}mm (rawW={w} rawH={h})");
        }
        catch (Exception e) { Console.WriteLine($"             cellshape read fail: {e.Message}"); }

        // 셀 배경 + 4변 테두리 — MCP 권위 경로:
        //   CellBorderFill → SelCellsBorderFill(선택 셀 BorderFill) → FillAttr(DrawFillAttr).WinBrushFaceColor
        try
        {
            var act = hwp.CreateAction("CellBorderFill");
            var pset = act.CreateSet();
            act.GetDefault(pset);
            dynamic sel = pset.Item("SelCellsBorderFill");   // 선택 셀의 BorderFill

            // fill — 3개 sub-BorderFill(Sel/All/Table) 의 FillAttr 을 모두 읽어 색이 있는 곳 확인
            foreach (var loc in new[] { "SelCellsBorderFill", "AllCellsBorderFill", "TableBorderFill" })
            {
                try
                {
                    dynamic bfset = pset.Item(loc);
                    dynamic fill = bfset.Item("FillAttr");
                    object? type = null, face = null;
                    try { type = fill.Item("Type"); } catch { }
                    try { face = fill.Item("WinBrushFaceColor"); } catch { }
                    int t = 0; try { t = Convert.ToInt32(type); } catch { }
                    if (t != 0 || face != null)
                        Console.WriteLine($"             bg[{loc}] Type={type} WinBrushFaceColor={DecColor(face)}");
                }
                catch { }
            }

            // borders (선택 셀)
            foreach (var sd in new[] { "Top", "Bottom", "Left", "Right" })
            {
                string colorKey = sd == "Left" ? "BorderCorlorLeft" : $"BorderColor{sd}";  // sic
                object? bt = null, bw = null, bc = null;
                try { bt = sel.Item($"BorderType{sd}"); } catch { }
                try { bw = sel.Item($"BorderWidth{sd}"); } catch { }
                try { bc = sel.Item(colorKey); } catch { }
                Console.WriteLine($"             border {sd,-6} type={bt} width={bw} color={DecColor(bc)}");
            }
        }
        catch (Exception e) { Console.WriteLine($"             cellborderfill read fail: {e.Message}"); }

        try { hwp.Run("Cancel"); } catch { }
    }

    try { Forge.Core.Linter.Range.SetCaretPos(hwpObj, origin); } catch { }
    Console.WriteLine("[tabledump] 완료 — 위 셀 텍스트로 박스를 식별해 서식 매핑.");
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────
// Cursor 모드 삽입 검증 — "커서 위에 삽입해도 아래 기존 내용은 무변경" 자동 판정.
//   1. FileNew → '아래 기존' 긴 글머리 삽입 (wrap 되지만 정렬 대상 아니어야 함)
//   2. MoveDocBegin (커서를 기존 내용 위로)
//   3. Cursor 모드 + Q 정렬로 작은 md 변환 (삽입 구간만 정렬돼야 함)
//   4. list 0 문단 전체를 순회하며 text + AlignType + Indent 덤프
//   판정: 삽입된 글머리/결론박스는 Indent<0(정렬됨), '아래 기존' 글머리는 Indent==0(무변경).
// ─────────────────────────────────────────────────────────────────────────
static int DiagnoseCursorInsert(string[] args)
{
    HwpSession session;
    try { session = HwpSessionHelpers.AttachOrCreate(visible: true, allowSpawn: false); }
    catch (NoExistingHwpException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 2; }
    catch (MultipleHwpInstancesException ex) { Console.Error.WriteLine($"[probe] {ex.Message}"); return 3; }
    Console.WriteLine($"[cursortest] attach: {session.VersionName} #{session.InstanceIndex}");
    dynamic hwp = session.Hwp;
    object hwpObj = hwp;

    hwp.Run("FileNew");

    // 인자로 위치 선택: "end"(기본, 흔한 경우 — 커서 아래 기존 내용 없음) / "mid"(중간 삽입).
    bool mid = args.Length > 1 && args[1].Equals("mid", StringComparison.OrdinalIgnoreCase);

    // 1) 기존 내용 (정렬 안 된 긴 □글머리). 판정 시 이 마커로 검색해 IndentAlign 이 닿았는지 본다.
    string exMarker = mid ? "△아래기존" : "△위기존";
    ComHelpers.InsertText(hwp,
        $"□ {exMarker} 이 문단은 {(mid ? "커서 아래" : "커서 위")}의 기존 내용으로서 충분히 길어 여러 줄로 wrap " +
        "되지만 새 내용을 삽입해도 이 문단의 들여쓰기는 0 으로 그대로 유지되어야 정상이며 IndentAlign 이 " +
        "이 문단을 처리(로그 [line])하면 기존 문서가 오염된 것이다");
    Console.WriteLine($"[cursortest] 기존 내용 삽입 완료 (모드={(mid ? "mid 중간삽입" : "end 끝삽입")})");

    // 2) 커서 위치: mid = 기존 내용 위(MoveDocBegin), end = 기존 내용 아래 새 줄(MoveDocEnd+BreakPara)
    if (mid)
    {
        hwp.Run("MoveDocBegin");
        Console.WriteLine("[cursortest] MoveDocBegin — 커서를 기존 내용 위로 (mid)");
    }
    else
    {
        hwp.Run("MoveDocEnd");
        hwp.Run("BreakPara");
        Console.WriteLine("[cursortest] MoveDocEnd+BreakPara — 커서를 기존 내용 아래로 (end)");
    }

    // 3) Cursor 모드 + Q 로 작은 md 변환
    string md =
        "1. 삽입 섹션\n\n" +
        "□ ★삽입글머리 이 문단은 커서 위치에 새로 삽입되는 내용으로 충분히 길어 wrap 되며 마커 뒤 " +
        "본문 기준으로 매달림 들여쓰기가 잡혀야(Indent<0) 정렬 성공이다\n\n" +
        "=> ★삽입결론 결론 박스 본문도 셀 안에서 여러 줄로 넘어갈 만큼 길게 작성하여 정렬이 적용되는지 확인한다";
    var doc = Forge.Core.Formatter.Parser.Parse(md);
    Console.WriteLine($"[cursortest] Cursor 모드 변환: {doc.Nodes.Count} 노드");
    Forge.Core.Formatter.HwpxWriter.LogFn lg = m => Console.WriteLine($"    {m}");
    Forge.Core.Formatter.HwpxWriter.GenerateHwpxViaCom(
        hwp, doc, outPath: "", spec: Forge.Core.Templates.ReportSpec.Report1, log: lg,
        mode: Forge.Core.Formatter.HwpxWriteMode.Cursor,
        applyIndentAlign: true, applyKerning: true);

    // 4) list 0 문단 전체 순회 — text + AlignType + Indent
    Console.WriteLine("[cursortest] ── 본문(list 0) 문단 판독 ──");
    hwp.Run("MoveDocBegin");
    int guard = 0; var seen = new HashSet<string>();
    bool belowOk = true, insertedAligned = false;
    while (guard++ < 500)
    {
        var pos = Forge.Core.Linter.Range.GetCaretPos(hwpObj);
        if (pos.List != 0) break;
        // 문단 텍스트
        hwp.Run("MoveParaBegin"); hwp.Run("MoveSelParaEnd");
        string t = Forge.Core.Renderers.Primitives.GetSelectionText(hwpObj, out _);
        hwp.Run("Cancel");
        // 문단 정렬/들여쓰기
        hwp.Run("MoveParaBegin");
        int indent = 0; int align = -1;
        try
        {
            var ps = hwp.HParameterSet.HParaShape;
            hwp.HAction.GetDefault("ParagraphShape", ps.HSet);
            align  = Convert.ToInt32(((object)ps).GetType().InvokeMember("AlignType", BindingFlags.GetProperty, null, ps, null) ?? -1);
            indent = Convert.ToInt32(((object)ps).GetType().InvokeMember("Indentation", BindingFlags.GetProperty, null, ps, null) ?? 0);
        }
        catch { }
        string vis = t.Trim(); if (vis.Length > 42) vis = vis[..42] + "…";
        if (vis.Length > 0)
            Console.WriteLine($"    para#{pos.Para} align={align} indent={indent}  '{vis}'");

        if (t.Contains(exMarker) && indent != 0) belowOk = false;
        if (t.Contains("★삽입") && indent < 0) insertedAligned = true;

        // 다음 문단
        var before = pos;
        hwp.Run("MoveNextParaBegin");
        var after = Forge.Core.Linter.Range.GetCaretPos(hwpObj);
        if (after == before || after.List != 0) break;
        string key = $"{after.Para}";
        if (!seen.Add(key)) break;
    }

    Console.WriteLine($"[cursortest] 판정: 삽입내용 정렬됨={insertedAligned}, '아래 기존' 무변경(indent=0)={belowOk}");
    Console.WriteLine(insertedAligned && belowOk
        ? "[cursortest] ✔ PASS — 커서 삽입 정렬 + 기존 내용 보존"
        : "[cursortest] ✘ FAIL — 위 판독 확인");
    return (insertedAligned && belowOk) ? 0 : 1;
}

// HWP COLORREF(0x00BBGGRR) → RGB 디코드. r=low byte.
static string DecColor(object? c)
{
    if (c == null) return "null";
    try
    {
        int v = Convert.ToInt32(c);
        int r = v & 0xFF, g = (v >> 8) & 0xFF, b = (v >> 16) & 0xFF;
        return $"RGB({r},{g},{b})[0x{v & 0xFFFFFF:X6}]";
    }
    catch { return c.ToString() ?? "?"; }
}

static string Vis(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

static string Cps(string s)
{
    if (string.IsNullOrEmpty(s)) return "[]";
    var parts = new List<string>();
    foreach (var r in s.EnumerateRunes()) parts.Add($"U+{r.Value:X4}");
    return "[" + string.Join(" ", parts) + "]";
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

// ─────────────────────────────────────────────────────────────────────────
// 파서 진단 — md 파일을 Parser.Parse 해 노드 타입을 덤프 (한/글 불필요, 순수 함수).
// 표 인식 등 파싱 동작 확인용.
// ─────────────────────────────────────────────────────────────────────────
static int ParseMarkdownFile(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("[probe] usage: parse <file.md>");
        return 64;
    }
    var path = Path.GetFullPath(args[1]);
    if (!File.Exists(path)) { Console.Error.WriteLine($"[probe] 파일 없음: {path}"); return 66; }

    var src = File.ReadAllText(path);
    var doc = Parser.Parse(src);
    Console.WriteLine($"[parse] {path}");
    Console.WriteLine($"[parse] {doc.Nodes.Count} 노드:");
    foreach (var n in doc.Nodes)
    {
        if (n.Type == NodeType.Table)
            Console.WriteLine($"  ★ Table — headers={n.Headers.Count} rows={n.Rows.Count} " +
                $"aligns=[{string.Join(",", n.Aligns)}]");
        else
        {
            var t = n.Text ?? "";
            if (t.Length > 40) t = t[..40] + "…";
            Console.WriteLine($"  {n.Type} marker='{n.Marker}' text='{t}'");
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
