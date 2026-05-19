// Forge 메인 윈도우.
// Python 원본 forge/ui/app.py 의 ForgeApp 1:1 이식.
//
// 구조:
//   - 상단 status bar (한/글 연결 상태 + About 버튼 + 한/글 선택)
//   - 중앙 TabControl (3 탭: 실시간 / 양식 / 마크다운)
//   - 하단 footer (버전 라벨)
//
// lazy 한/글 attach — 사용자 액션 시점 (Tab 의 변환·룰 버튼) 에 EnsureHwp().

using Forge.Core;
using Forge.UI.Tabs;

namespace Forge.UI;

public partial class MainForm : Form
{
    public AppState State { get; } = new();

    private TabControl _tabs = null!;
    private Label _statusLabel = null!;
    private Button _hwpPickButton = null!;
    private Label _versionLabel = null!;

    private RealtimeTab _realtimeTab = null!;

    public MainForm()
    {
        Text = "Forge";
        ClientSize = new Size(1200, 900);
        MinimumSize = new Size(960, 700);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUI();
        UpdateStatus();

        // hotkey 등록은 윈도우 핸들 만들어진 후 — Shown 시점.
        Shown += (_, _) => _realtimeTab.StartHotkeys();
        FormClosing += (_, _) => _realtimeTab.StopHotkeys();
    }

    private void BuildUI()
    {
        // 상단 status bar
        var statusBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = SystemColors.Control,
            Padding = new Padding(8, 6, 8, 6),
        };
        _statusLabel = new Label
        {
            AutoSize = true,
            Location = new Point(8, 8),
            Text = "한/글 미연결 (lazy attach)",
        };
        _hwpPickButton = new Button
        {
            Text = "한/글 선택",
            AutoSize = true,
            Location = new Point(560, 4),
        };
        _hwpPickButton.Click += OnHwpPick;
        var aboutButton = new Button
        {
            Text = "?",
            Size = new Size(28, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        aboutButton.Click += OnAbout;
        // 우측 정렬 — Resize 시 reposition
        void RepositionStatus()
        {
            aboutButton.Location = new Point(statusBar.Width - 36, 4);
        }
        statusBar.Resize += (_, _) => RepositionStatus();
        RepositionStatus();
        statusBar.Controls.Add(_statusLabel);
        statusBar.Controls.Add(_hwpPickButton);
        statusBar.Controls.Add(aboutButton);
        Controls.Add(statusBar);

        // 하단 footer
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            BackColor = SystemColors.Control,
            Padding = new Padding(8, 4, 8, 4),
        };
        _versionLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            Text = $"v{ForgeVersion.Version} — github.com/kinphw/forge",
            ForeColor = Color.Gray,
        };
        footer.Controls.Add(_versionLabel);
        Controls.Add(footer);

        // 중앙 탭
        _tabs = new TabControl { Dock = DockStyle.Fill };
        _realtimeTab = new RealtimeTab(State, UpdateStatus) { Text = "① 실시간 작업" };
        _tabs.TabPages.Add(_realtimeTab);
        _tabs.TabPages.Add(new TemplatesTab(State) { Text = "② 양식" });
        _tabs.TabPages.Add(new MarkdownTab(State, UpdateStatus) { Text = "③ 마크다운 입력" });
        Controls.Add(_tabs);

        // Controls.Add 순서: 마지막 추가가 z-order 최상위. 탭이 status 와 footer 사이를 채우게 됨.
        // 정렬: Top(status) → Bottom(footer) → Fill(tabs)
        _tabs.BringToFront();
    }

    /// <summary>status bar 의 한/글 연결 상태 라벨 갱신.</summary>
    public void UpdateStatus()
    {
        if (State.Hwp is null)
        {
            _statusLabel.Text = "한/글 미연결 (lazy attach — 첫 변환 시 자동)";
            _statusLabel.ForeColor = Color.DimGray;
        }
        else
        {
            _statusLabel.Text = $"한/글 연결: {State.Hwp.VersionName} #{State.Hwp.InstanceIndex}";
            _statusLabel.ForeColor = Color.DarkGreen;
        }
    }

    private void OnHwpPick(object? sender, EventArgs e)
    {
        var instances = HwpSessionHelpers.ListInstances();
        if (instances.Count == 0)
        {
            MessageBox.Show(this,
                "ROT 에 등록된 한/글 인스턴스 없음.\n한/글을 먼저 실행한 뒤 다시 시도해주세요.",
                "한/글 선택", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var picker = new HwpPickerForm(instances);
        if (picker.ShowDialog(this) == DialogResult.OK && picker.Selected is { } chosen)
        {
            State.Hwp = HwpSessionHelpers.AttachToInstance(chosen);
            State.PreferredMoniker = chosen.MonikerName;
            UpdateStatus();
        }
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        MessageBox.Show(this,
            $"Forge — github.com/kinphw/forge\n" +
            $"버전: {ForgeVersion.Version}\n" +
            $"작성자: kinphw\n\n" +
            $"한/글 보고서 자동 정형 도구.\n" +
            $"개조식 markdown → .hwpx 변환 + 활성 문서 정형 룰.",
            "Forge", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>다른 탭에서 한/글 lazy attach 시 호출 — 다중 인스턴스면 picker.</summary>
    public bool EnsureHwp(bool allowSpawn = false)
    {
        if (State.Hwp is not null && HwpSessionHelpers.IsAlive(State.Hwp.Hwp))
            return true;

        try
        {
            State.Hwp = HwpSessionHelpers.AttachOrCreate(
                visible: true,
                allowSpawn: allowSpawn,
                preferMoniker: State.PreferredMoniker);
            UpdateStatus();
            return true;
        }
        catch (MultipleHwpInstancesException ex)
        {
            using var picker = new HwpPickerForm(ex.Instances);
            if (picker.ShowDialog(this) == DialogResult.OK && picker.Selected is { } chosen)
            {
                State.Hwp = HwpSessionHelpers.AttachToInstance(chosen);
                State.PreferredMoniker = chosen.MonikerName;
                UpdateStatus();
                return true;
            }
            return false;
        }
        catch (NoExistingHwpException ex)
        {
            MessageBox.Show(this, ex.Message, "한/글 연결 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }
}

/// <summary>버전 단일 진실원본 — Python forge/__init__.py 등가.</summary>
public static class ForgeVersion
{
    public const string Version = "0.4.0";  // C# 포팅 — Python 0.3.3 후속
}
