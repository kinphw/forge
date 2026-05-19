// Forge.UI 진입점.
// Python run.pyw 등가물. WinForms 표준 패턴 — STA + High DPI + 메인 폼 실행.

namespace Forge.UI;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();  // High DPI + GDI compat (WinForms default)
        Application.Run(new MainForm());
    }
}
