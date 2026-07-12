// Forge 메인 윈도우.
// Python 원본 forge/ui/app.py 의 ForgeApp.
//
// 구조:
//   상단 status bar (한/글 연결 상태 + 한/글 선택 + About)
//   중앙 TabControl (5 탭: How to / 실시간 / 상용구 / 양식 / 마크다운)
//   하단 footer (버전 라벨)

using System.Reflection;
using Forge.Core;
using Forge.UI.Tabs;

namespace Forge.UI;

public partial class MainForm : Form
{
    public AppState State { get; } = new();

    private TabControl _tabs = null!;
    private Label _statusLabel = null!;
    private CircularStatusDot _statusDot = null!;
    private Button _hwpPickButton = null!;
    private RealtimeTab _realtimeTab = null!;
    private MarkdownTab _markdownTab = null!;

    public RealtimeTab? GetRealtimeTab() => _realtimeTab;
    public MarkdownTab? GetMarkdownTab() => _markdownTab;

    public MainForm()
    {
        Text = "Forge";
        ClientSize = new Size(1240, 920);
        MinimumSize = new Size(1000, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ForgeTheme.Background;
        Font = ForgeTheme.Body();
        try { Icon = ForgeIcon.Build(); } catch { /* 아이콘 실패는 치명적 아님 */ }

        BuildUI();
        UpdateStatus();

        Shown += (_, _) => _realtimeTab.StartHotkeys();
        FormClosing += (_, _) =>
        {
            FlushAllSettings();           // 디바운스 잔량 강제 저장
            _realtimeTab.StopHotkeys();
        };
    }

    private void BuildUI()
    {
        // 하단 footer 먼저 (Dock 순서)
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            BackColor = ForgeTheme.PanelBg,
            Padding = new Padding(ForgeTheme.PadLg, 4, ForgeTheme.PadLg, 4),
        };
        var versionLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            Text = $"Forge v{ForgeVersion.Version}  ·  github.com/kinphw/forge",
            Font = ForgeTheme.Small(),
            ForeColor = ForgeTheme.TextMuted,
        };
        footer.Controls.Add(versionLabel);
        Controls.Add(footer);

        // 상단 status bar
        var statusBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,           // 버튼(y=7 + 32px) 의 하단이 TabControl underline 과 겹치지 않게 여유
            BackColor = ForgeTheme.PanelBg,
            Padding = new Padding(ForgeTheme.PadLg, 8, ForgeTheme.PadLg, 8),
        };
        // 좌측: 상태 dot + 라벨 — dot 은 OwnerDraw 둥근 원, 라벨은 BodyBold 로
        // 위계 강조 (한/글 연결 여부가 한 눈에 들어와야 함).
        _statusDot = new CircularStatusDot
        {
            Size = new Size(12, 12),
            Location = new Point(ForgeTheme.PadLg, 16),
            DotColor = ForgeTheme.TextMuted,
        };
        _statusLabel = new Label
        {
            AutoSize = true,
            Font = ForgeTheme.BodyBold(),
            ForeColor = ForgeTheme.TextPrimary,
            Location = new Point(ForgeTheme.PadLg + 18, 13),
            Text = "한/글 미연결",
        };
        statusBar.Controls.Add(_statusDot);
        statusBar.Controls.Add(_statusLabel);

        // 우측 (right→left 순서): About / 한/글 선택 / 설정 저장
        _hwpPickButton = new Button { Text = "한/글 선택" };
        ForgeTheme.StyleFlatButton(_hwpPickButton, glyph: MdlIcon.Switch);
        _hwpPickButton.Click += OnHwpPick;

        var saveBtn = new Button { Text = "설정 저장" };
        ForgeTheme.StyleFlatButton(saveBtn, glyph: MdlIcon.Save);
        saveBtn.Click += (_, _) =>
        {
            FlushAllSettings();
            _statusLabel.Text = "모든 설정 저장 완료 — %APPDATA%\\Forge\\settings.json";
            _statusLabel.ForeColor = ForgeTheme.Success;
        };

        var aboutButton = new Button { Text = "", MinimumSize = new Size(34, 30) };
        ForgeTheme.StyleFlatButton(aboutButton, glyph: MdlIcon.Info);
        aboutButton.Padding = new Padding(8, 4, 4, 4);   // icon-only 정렬
        aboutButton.Click += OnAbout;

        void RepositionRight()
        {
            aboutButton.Location = new Point(statusBar.Width - aboutButton.Width - ForgeTheme.PadLg, 7);
            _hwpPickButton.Location = new Point(aboutButton.Left - _hwpPickButton.Width - 8, 7);
            saveBtn.Location = new Point(_hwpPickButton.Left - saveBtn.Width - 8, 7);
        }
        statusBar.Resize += (_, _) => RepositionRight();
        statusBar.Controls.Add(saveBtn);
        statusBar.Controls.Add(_hwpPickButton);
        statusBar.Controls.Add(aboutButton);

        Controls.Add(statusBar);

        // 중앙 탭 — OwnerDraw 로 active tab underline + Accent 텍스트
        _tabs = new TabControl { Dock = DockStyle.Fill };
        ForgeTheme.StyleTabControl(_tabs);
        _realtimeTab = new RealtimeTab(State, UpdateStatus) { Text = "실시간 작업" };
        _markdownTab = new MarkdownTab(State, UpdateStatus) { Text = "마크다운 입력" };

        _tabs.TabPages.Add(new HowToTab { Text = "How to?" });
        _tabs.TabPages.Add(_realtimeTab);
        _tabs.TabPages.Add(new GlossaryTab { Text = "상용구" });
        _tabs.TabPages.Add(new TemplatesTab(State) { Text = "양식삽입" });
        _tabs.TabPages.Add(_markdownTab);
        _tabs.SelectedIndex = 1;  // 시작 탭 = 실시간

        Controls.Add(_tabs);
        _tabs.BringToFront();

        // 초기 위치 잡기
        statusBar.PerformLayout();
        OnResize(EventArgs.Empty);
    }

    /// <summary>한/글 연결 상태 라벨 갱신. attach 된 활성 문서 파일명도 표시.</summary>
    public void UpdateStatus()
    {
        if (State.Hwp is null)
        {
            _statusDot.DotColor = ForgeTheme.TextMuted;
            _statusLabel.Text = "한/글 미연결 (첫 변환 시 자동 attach)";
            _statusLabel.ForeColor = ForgeTheme.TextMuted;
        }
        else
        {
            _statusDot.DotColor = ForgeTheme.Success;
            _statusLabel.Text = $"한/글 연결: {State.Hwp.VersionName}  #{State.Hwp.InstanceIndex}{FileSuffix()}";
            _statusLabel.ForeColor = ForgeTheme.TextPrimary;
        }
    }

    /// <summary>활성 문서 basename — `" — report.hwpx"` 또는 `" — (새 문서)"`.
    /// best-effort: hwp.Path 가 빈 문자열이거나 dispatch 실패 시 빈 suffix.</summary>
    private string FileSuffix()
    {
        try
        {
            dynamic hwp = State.Hwp!.Hwp;
            string p = (string?)hwp.Path ?? "";
            if (string.IsNullOrEmpty(p)) return " — (새 문서)";
            return " — " + Path.GetFileName(p);
        }
        catch { return ""; }
    }

    /// <summary>모든 탭의 debounce 잔량 강제 flush — 종료 / 💾 버튼 / picker 후.</summary>
    public void FlushAllSettings()
    {
        try { _realtimeTab.FlushPersist(); } catch { }
        try { _markdownTab.FlushPersist(); } catch { }
    }

    /// <summary>현재 활성 탭의 로그 영역에 한 줄 출력 (지원하는 탭만).
    /// picker/attach 결과 안내를 MessageBox 대신 로그로 흘려보내는 경로.</summary>
    public void LogToActive(string msg)
    {
        var page = _tabs.SelectedTab;
        if (ReferenceEquals(page, _markdownTab)) _markdownTab.Log(msg);
        else if (ReferenceEquals(page, _realtimeTab)) _realtimeTab.Log(msg);
        // HowTo / Templates 탭에는 로그 영역 없음 — silent
    }

    private void OnHwpPick(object? sender, EventArgs e)
    {
        var instances = HwpSessionHelpers.ListInstances();
        if (instances.Count == 0)
        {
            LogToActive("[한/글 선택] ROT 에 등록된 인스턴스 없음 — 한/글을 먼저 실행해 주세요.");
            return;
        }
        using var picker = new HwpPickerForm(instances);
        if (picker.ShowDialog(this) == DialogResult.OK && picker.Selected is { } chosen)
        {
            State.Hwp = HwpSessionHelpers.AttachToInstance(chosen);
            State.PreferredMoniker = chosen.MonikerName;
            UpdateStatus();
            LogToActive($"[한/글 선택] {chosen.DisplayLabel} 선택됨 — 작업 버튼을 다시 눌러 주세요.");
        }
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        using var about = new AboutForm();
        about.ShowDialog(this);
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
                LogToActive($"[한/글 선택] {chosen.DisplayLabel} 선택됨 — 작업 버튼을 다시 눌러 주세요.");
                return true;
            }
            return false;
        }
        catch (NoExistingHwpException ex)
        {
            LogToActive($"[한/글 연결] ✘ {ex.Message}");
            return false;
        }
    }
}

public static class ForgeVersion
{
    // SSOT: Directory.Build.props 의 <Version>. 빌드 시 InformationalVersion
    // attribute 로 박힘. 여기선 reflection 으로 읽기만 — manual sync 불필요.
    // build metadata (`+commit-hash`) 가 있으면 잘라내고 semver 만 노출.
    public static readonly string Version =
        (typeof(ForgeVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0").Split('+')[0];
}

/// <summary>About 다이얼로그 — MessageBox 대신 단정한 폼.</summary>
public sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "Forge 정보";
        ClientSize = new Size(440, 280);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = ForgeTheme.PanelBg;
        Font = ForgeTheme.Body();

        // 본문 = TopDown FlowLayout (DockFill), 하단 = Anchor=Right 정렬 버튼 패널.
        // 직전 구조(닫기까지 같은 FlowLayout에 넣고 Anchor=Right) 는 FlowLayout
        // 안에서 Anchor 가 무력해 닫기가 좌측에 박혔음 — 별도 Panel 로 분리.
        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 24, 24, 8),
            AutoSize = false,
        };

        content.Controls.Add(new Label
        {
            Text = "Forge",
            Font = new Font(ForgeTheme.H1().FontFamily, 22f, FontStyle.Bold),
            ForeColor = ForgeTheme.Accent,
            AutoSize = true,
        });
        content.Controls.Add(new Label
        {
            Text = $"v{ForgeVersion.Version}",
            Font = ForgeTheme.Body(),
            ForeColor = ForgeTheme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16),
        });
        content.Controls.Add(new Label
        {
            Text = "한/글 보고서 자동 편집기\n" +
                   "개조식 markdown → .hwpx 변환 + 활성 문서 정형룰 적용\n",
            Font = ForgeTheme.Body(),
            ForeColor = ForgeTheme.TextPrimary,
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(0, 0, 0, 16),
        });
        content.Controls.Add(new Label
        {
            Text = "작성자: kinphw\nhttps://github.com/kinphw/forge",
            Font = ForgeTheme.Small(),
            ForeColor = ForgeTheme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        });

        var buttonRow = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(24, 8, 24, 16),
            BackColor = ForgeTheme.PanelBg,
        };
        var closeBtn = new Button { Text = "닫기", DialogResult = DialogResult.OK };
        ForgeTheme.StyleFlatButton(closeBtn, accent: true);
        closeBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        closeBtn.Location = new Point(buttonRow.ClientSize.Width - 92, 8);
        closeBtn.MinimumSize = new Size(80, 30);
        buttonRow.Resize += (_, _) =>
            closeBtn.Location = new Point(buttonRow.ClientSize.Width - closeBtn.Width - 24, 8);
        buttonRow.Controls.Add(closeBtn);
        AcceptButton = closeBtn;
        CancelButton = closeBtn;

        Controls.Add(content);
        Controls.Add(buttonRow);
    }
}
