// Win32 P/Invoke 정의 — RegisterHotKey 메시지 펌프 + 스레드 메시지 전송.
// Python ctypes.windll.user32 등가물.

using System.Runtime.InteropServices;

namespace Forge.Win32;

public static class NativeMethods
{
    // Modifier bits (RegisterHotKey)
    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CONTROL  = 0x0002;
    public const uint MOD_SHIFT    = 0x0004;
    public const uint MOD_WIN      = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;  // Windows 7+

    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_QUIT   = 0x0012;
    // 사용자 정의 메시지 — WM_APP(0x8000) 이상은 application-defined 영역
    public const uint WM_APP_RELOAD = 0x8000 + 1;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint   message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint   time;
        public POINT  pt;
    }

    // ── 키 입력 주입 (상용구 Ctrl+Shift+I 전처리) ──────────────────────
    // 전역 단축키는 포커스를 뺏지 않으므로 대상 앱(한/글)의 IME 조합이 살아있음.
    // 오른쪽 방향키를 주입하면 IME 가 조합을 확정(표준 동작)하고 캐럿이 확정 글자
    // 뒤로 이동 → 이후 "앞 글자 읽기"로 준말을 잡는다. 포커스/창 조작 없음(깜박임 0).
    // keybd_event: 구식이나 단일 P/Invoke 로 충분 (INPUT 유니언 마샬링 함정 회피).

    public const byte VK_RIGHT       = 0x27;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // ── 전역 키 상태 (UI 스레드 점유 루프 중 ESC 취소 감지) ──────────────
    // 자간 좁힘(W) 등 긴 동기 루프는 UI 스레드를 점유해 일반 KeyDown 이 안 뜬다.
    // 루프가 매 iter GetAsyncKeyState(ESC) 를 폴링해 사용자 취소를 즉시 잡는다.

    public const int VK_ESCAPE = 0x1B;

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
}
