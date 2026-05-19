// 앱 아이콘 — 프로그램으로 생성 (외부 .ico 파일 X).
// Python 원본 forge/ui/icon.py 와 동일한 컨셉:
//   64×64 캔버스에 사각형 3 개로 "F" 글리프 합성.
//
// 색상 컨셉:
//   background = slate-800 (#1f2937)
//   foreground = amber-500 (#f59e0b)  — Forge "불꽃"

namespace Forge.UI;

internal static class ForgeIcon
{
    private static readonly Color Bg = Color.FromArgb(0x1f, 0x29, 0x37);
    private static readonly Color Fg = Color.FromArgb(0xf5, 0x9e, 0x0b);

    /// <summary>64×64 "F" 아이콘을 Icon 으로 반환. Form.Icon 에 직접 할당 가능.</summary>
    public static Icon Build()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Bg);
            using var brush = new SolidBrush(Fg);
            // F stem
            g.FillRectangle(brush, 14, 12, 10, 40);
            // F top bar
            g.FillRectangle(brush, 14, 12, 36, 8);
            // F middle bar
            g.FillRectangle(brush, 14, 28, 28, 7);
        }
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            // 복제해서 반환 (원본 hIcon GC 와 무관하게 살아있는 Icon).
            return (Icon)tmp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [System.Runtime.InteropServices.DefaultDllImportSearchPaths(
            System.Runtime.InteropServices.DllImportSearchPath.System32)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
