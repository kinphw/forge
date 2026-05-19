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
}
