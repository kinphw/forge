// Win32 시스템 전역 hotkey 매니저.
// Python 원본 forge/ui/hotkeys.py 의 1:1 포팅.
//
// WinForms 의 KeyDown 은 폼 포커스 시에만 동작. 사용자가 한/글에서 작업하면서
// Forge 룰을 hotkey 로 호출하려면 OS 전역 hotkey 필요.
//
// Win32 RegisterHotKey + GetMessage 메시지 펌프를 별도 스레드에서 돌리고,
// WM_HOTKEY 수신 시 mainForm.BeginInvoke(callback) 으로 UI 스레드에 dispatch.
//
// ★ 동시 등록: Win32 제한 없으나 동일 조합을 다른 프로세스가 잡고 있으면
//   RegisterHotKey 실패 → log 만 남기고 다른 hotkey 는 정상 동작.
//   Start() 가 등록 결과 리스트 반환.

using System.Collections.Concurrent;
using System.Windows.Forms;
using static Forge.Win32.NativeMethods;

namespace Forge.Win32;

public sealed class HotkeyDef
{
    public int HkId { get; }
    public uint Modifiers { get; }
    public uint? Vk { get; set; }   // null = 비활성화 상태로 등록 (RegisterHotKey skip)
    public Action Callback { get; }
    public string Label { get; set; }
    public bool Registered { get; internal set; }

    public HotkeyDef(int hkId, uint modifiers, uint? vk, Action callback, string label)
    {
        HkId = hkId;
        Modifiers = modifiers;
        Vk = vk;
        Callback = callback;
        Label = label;
    }
}

public sealed class GlobalHotkeyManager : IDisposable
{
    private readonly Control _uiRoot;  // BeginInvoke target
    private readonly List<HotkeyDef> _defs = new();
    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _stopEvent = new(false);
    // 동적 변경 명령 큐 — UI 스레드 → 펌프 스레드 (replace 등)
    private readonly ConcurrentQueue<Action> _cmdQueue = new();
    private bool _disposed;

    public GlobalHotkeyManager(Control uiRoot)
    {
        _uiRoot = uiRoot;
    }

    /// <summary>
    /// hotkey 정의 추가. Start() 전에 호출.
    /// vk=null 이면 비활성화 — RegisterHotKey skip (사용자가 settings 에서 명시 비활성화한 hotkey 표현).
    /// </summary>
    public void Add(int hkId, uint modifiers, uint? vk, Action callback, string label = "")
    {
        if (_thread is not null) throw new InvalidOperationException("Start() 후엔 Add() 불가");
        _defs.Add(new HotkeyDef(hkId, modifiers, vk, callback, label));
    }

    /// <summary>
    /// 백그라운드 스레드 시작. 등록 결과 [(label, ok), ...] 반환.
    /// ok=false 는 다른 앱이 이미 잡고 있는 조합.
    /// </summary>
    public List<(string Label, bool Ok)> Start()
    {
        if (_thread is not null) throw new InvalidOperationException("이미 Start() 됨");

        var results = new List<(string, bool)>();
        var readyEvent = new ManualResetEventSlim(false);

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();

            // RegisterHotKey 모두 시도
            foreach (var d in _defs)
            {
                if (d.Vk is null)
                {
                    d.Registered = false;
                    results.Add((d.Label, false));
                    continue;
                }
                bool ok = RegisterHotKey(IntPtr.Zero, d.HkId, d.Modifiers | MOD_NOREPEAT, d.Vk.Value);
                d.Registered = ok;
                results.Add((d.Label, ok));
            }
            readyEvent.Set();

            // 메시지 펌프 — WM_HOTKEY 와 WM_APP_RELOAD 처리
            while (!_stopEvent.IsSet)
            {
                int got = GetMessageW(out MSG msg, IntPtr.Zero, 0, 0);
                if (got <= 0) break;  // WM_QUIT(0) 또는 에러(-1)

                if (msg.message == WM_HOTKEY)
                {
                    int hkId = msg.wParam.ToInt32();
                    var d = _defs.FirstOrDefault(x => x.HkId == hkId);
                    if (d is not null)
                    {
                        // UI 스레드에 dispatch
                        try
                        {
                            if (_uiRoot.IsHandleCreated && !_uiRoot.IsDisposed)
                                _uiRoot.BeginInvoke(d.Callback);
                        }
                        catch { /* dispatch 실패 — 폼 disposed 등, 무시 */ }
                    }
                }
                else if (msg.message == WM_APP_RELOAD)
                {
                    // 동적 변경 명령 처리
                    while (_cmdQueue.TryDequeue(out var cmd))
                    {
                        try { cmd(); } catch { /* command 실패 — 무시 */ }
                    }
                }
            }

            // 종료 — 등록한 hotkey 해제
            foreach (var d in _defs)
            {
                if (d.Registered) UnregisterHotKey(IntPtr.Zero, d.HkId);
            }
        })
        {
            IsBackground = true,
            Name = "Forge.GlobalHotkeyManager",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        readyEvent.Wait(TimeSpan.FromSeconds(5));
        return results;
    }

    /// <summary>
    /// 동적 변경 — hotkey 의 vk/label 교체. 펌프 스레드에서 처리됨.
    /// 반환: 새 등록 성공 여부.
    /// </summary>
    public bool Replace(int hkId, uint? newVk, string? newLabel = null)
    {
        var d = _defs.FirstOrDefault(x => x.HkId == hkId);
        if (d is null) return false;

        var result = new TaskCompletionSource<bool>();
        _cmdQueue.Enqueue(() =>
        {
            try
            {
                if (d.Registered)
                {
                    UnregisterHotKey(IntPtr.Zero, d.HkId);
                    d.Registered = false;
                }
                d.Vk = newVk;
                if (newLabel is not null) d.Label = newLabel;
                if (newVk is not null)
                {
                    bool ok = RegisterHotKey(IntPtr.Zero, d.HkId, d.Modifiers | MOD_NOREPEAT, newVk.Value);
                    d.Registered = ok;
                    result.TrySetResult(ok);
                }
                else
                {
                    result.TrySetResult(true);
                }
            }
            catch { result.TrySetResult(false); }
        });
        if (_thread is not null)
            PostThreadMessageW(_threadId, WM_APP_RELOAD, IntPtr.Zero, IntPtr.Zero);
        return result.Task.Wait(TimeSpan.FromSeconds(2)) && result.Task.Result;
    }

    /// <summary>스레드 종료 + 모든 hotkey 해제.</summary>
    public void Stop()
    {
        if (_thread is null) return;
        _stopEvent.Set();
        if (_threadId != 0)
            PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _stopEvent.Dispose();
    }

    /// <summary>현재 등록 상태 dump (UI 표시용).</summary>
    public IReadOnlyList<HotkeyDef> Definitions => _defs;
}

/// <summary>표준 Virtual-Key 코드 헬퍼 — ASCII letter → VK code.</summary>
public static class VirtualKey
{
    public static uint Letter(char c) => (uint)char.ToUpperInvariant(c);
}
