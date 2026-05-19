// 탭 ② 양식삽입 — 고정 양식 11종을 활성 한/글 문서 커서 위치에 emit.
// Python 원본 forge/ui/tabs/templates_tab.py 1:1 포팅.

using Forge.Core.Templates;

namespace Forge.UI.Tabs;

public sealed class TemplatesTab : TabPage
{
    private readonly AppState _state;
    private readonly ToolTip _tooltip = new()
    {
        AutoPopDelay = 12000,
        InitialDelay = 400,
        ShowAlways = true,
    };
    private TextBox _log = null!;

    public TemplatesTab(AppState state)
    {
        _state = state;
        BackColor = ForgeTheme.Background;
        Padding = ForgeTheme.PanelPadding;
        BuildUI();
    }

    private MainForm? Main => FindForm() as MainForm;

    private void BuildUI()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = ForgeTheme.Background,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 헤더
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 설명
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 카탈로그
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160)); // 로그

        root.Controls.Add(ForgeTheme.SectionTitle("양식삽입"), 0, 0);
        root.Controls.Add(ForgeTheme.SectionDesc(
            "고정된 보고서 양식을 활성 한/글 문서의 현재 커서 위치에 삽입.\n" +
            "버튼 클릭 시 양식 emit. placeholder 글자 (◆◆◆ 등) 는 사용자가 한/글에서 직접 교체."),
            0, 1);

        root.Controls.Add(BuildCatalog(), 0, 2);
        root.Controls.Add(BuildLogPanel(), 0, 3);

        Controls.Add(root);
    }

    private Control BuildCatalog()
    {
        var box = new GroupBox
        {
            Text = "🧪 활성 한/글 문서의 현재 커서 위치에 삽입됨",
            Dock = DockStyle.Fill,
            AutoSize = false,
        };
        ForgeTheme.StyleGroup(box);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ForgeTheme.GroupBg };
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4),
        };
        scroll.Controls.Add(flow);
        box.Controls.Add(scroll);

        string? prevGroup = null;
        foreach (var e in ForgeTemplates.All)
        {
            // 그룹 변경 지점 — separator + 그룹 라벨
            if (prevGroup is not null && e.Group != prevGroup)
            {
                flow.Controls.Add(new Panel
                {
                    Height = 1,
                    Width = 700,
                    BackColor = ForgeTheme.Border,
                    Margin = new Padding(0, 6, 0, 6),
                });
            }
            if (prevGroup != e.Group)
            {
                flow.Controls.Add(new Label
                {
                    Text = "▾ " + e.Group,
                    AutoSize = true,
                    Font = ForgeTheme.BodyBold(),
                    ForeColor = ForgeTheme.Accent,
                    Margin = new Padding(0, 8, 0, 4),
                });
            }
            prevGroup = e.Group;

            flow.Controls.Add(BuildEntryRow(e));
        }
        return box;
    }

    private Panel BuildEntryRow(ForgeTemplates.TemplateEntry e)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 2, 0, 2),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var btn = new Button
        {
            Text = $"[{e.Num}] {e.Label}",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(240, 30),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
        };
        ForgeTheme.StyleFlatButton(btn);
        btn.Click += (_, _) => SafeInvoke(e);
        _tooltip.SetToolTip(btn, $"{e.Label} — {e.Description}");
        row.Controls.Add(btn, 0, 0);

        var desc = new Label
        {
            Text = e.Description,
            AutoSize = true,
            Font = ForgeTheme.Small(),
            ForeColor = ForgeTheme.TextMuted,
            Margin = new Padding(8, 8, 0, 0),
        };
        row.Controls.Add(desc, 1, 0);
        return row;
    }

    private Panel BuildLogPanel()
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, ForgeTheme.Pad, 0, 0) };
        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = ForgeTheme.LogBg,
            ForeColor = ForgeTheme.LogText,
            Font = ForgeTheme.Mono(),
            BorderStyle = BorderStyle.FixedSingle,
        };
        p.Controls.Add(_log);
        return p;
    }

    private void SafeInvoke(ForgeTemplates.TemplateEntry e)
    {
        if (Main is null) return;
        if (!Main.EnsureHwp(allowSpawn: false))
        {
            Log($"[{e.Num}] {e.Label} ✘ 한/글 attach 실패 — 한/글 먼저 실행해 주세요.");
            return;
        }
        if (_state.Hwp is null) return;
        try
        {
            Log($"[{e.Num}] {e.Label} 시작");
            e.Invoke(_state.Hwp.Hwp);
            Log($"  ✔ [{e.Num}] {e.Label} 완료");
        }
        catch (Exception ex)
        {
            Log($"  ✘ [{e.Num}] {e.Label} 실패: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Log(string msg)
    {
        if (_log is null || _log.IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => Log(msg))); return; }
        _log.AppendText(msg + Environment.NewLine);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}
