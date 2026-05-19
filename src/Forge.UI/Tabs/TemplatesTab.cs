// 탭 ② 양식 — 보고서 spec 편집 (폰트·여백·글머리 등).
// Python 원본 forge/ui/tabs/templates_tab.py 등가. W4g 에서 채울 예정.

namespace Forge.UI.Tabs;

public sealed class TemplatesTab : TabPage
{
    private readonly AppState _state;

    public TemplatesTab(AppState state)
    {
        _state = state;
        BuildUI();
    }

    private void BuildUI()
    {
        var label = new Label
        {
            AutoSize = true,
            Location = new Point(16, 16),
            Text = "탭 ② 양식 — W4g 에서 spec 편집 UI 채울 예정",
            ForeColor = Color.DimGray,
        };
        Controls.Add(label);
    }
}
