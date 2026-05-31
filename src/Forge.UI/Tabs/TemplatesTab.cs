// 탭 ② 양식삽입 — 양식 20 종을 활성 한/글 문서 커서 위치에 emit.
// 호버 시 미리보기 PNG 를 우측 패널 PictureBox 에 표시 (별도 popup form 아님 — 탭 헤더
// 깜빡임 회피용으로 임베드 컨트롤 채택).
//
// PNG 경로: <exe-dir>/resources/template-previews/{NN}.png  (NN = TemplateEntry.Num, 2자리).
// 파일 없으면 직전 표시 유지 (기호류 14~20 등 — 시각 깜빡임 없음).

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

    // 호버 미리보기 — 우측 패널의 임베드 PictureBox + placeholder Label.
    private PictureBox _previewPicture = null!;
    private Label _previewLabel = null!;
    private readonly Dictionary<int, Image?> _previewCache = new();  // num → Image (null = 파일 없음)

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

        // 카탈로그(좌) + 미리보기 패널(우) 2-column split.
        var middle = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        middle.Controls.Add(BuildCatalog(), 0, 0);
        middle.Controls.Add(BuildPreviewPanel(), 1, 0);
        root.Controls.Add(middle, 0, 2);

        root.Controls.Add(BuildLogPanel(), 0, 3);

        Controls.Add(root);
    }

    private Control BuildPreviewPanel()
    {
        var box = new GroupBox
        {
            Text = "미리보기 (양식 호버)",
            Dock = DockStyle.Fill,
            Margin = new Padding(ForgeTheme.Pad, 0, 0, 0),
        };
        ForgeTheme.StyleGroup(box);

        _previewPicture = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
        };
        _previewLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "양식 버튼에 마우스를 올리면 미리보기가 표시됩니다.",
            Font = ForgeTheme.Body(),
            ForeColor = ForgeTheme.TextMuted,
            BackColor = Color.White,
        };
        // 두 컨트롤 모두 Dock=Fill — label 을 위로 z-order 올려 image 없을 때 보임,
        // image 설정 시 label.Visible=false 로 가림.
        box.Controls.Add(_previewPicture);
        box.Controls.Add(_previewLabel);
        _previewLabel.BringToFront();
        return box;
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

        // 호버 시 우측 미리보기 패널 갱신. PNG 없는 양식 (기호류 14~20) 은 직전 표시 유지
        // (PictureBox.Image 변경 없음) — 깜빡임 / 빈 화면 회피.
        void Enter(object? _, EventArgs _2)
        {
            var img = LoadPreview(e.Num);
            if (img is null) return;
            _previewPicture.Image = img;
            _previewLabel.Visible = false;
        }
        void Leave(object? _, EventArgs _2) { /* 직전 미리보기 유지 — leave 시 클리어 안 함 */ }
        btn.MouseEnter += Enter;
        btn.MouseLeave += Leave;

        row.Controls.Add(btn, 0, 0);

        var desc = new Label
        {
            Text = e.Description,
            AutoSize = true,
            Font = ForgeTheme.Small(),
            ForeColor = ForgeTheme.TextMuted,
            Margin = new Padding(8, 8, 0, 0),
        };
        desc.MouseEnter += Enter;
        desc.MouseLeave += Leave;
        row.Controls.Add(desc, 1, 0);
        return row;
    }

    /// <summary>
    /// 양식 미리보기 PNG 로드 (cache).
    /// 경로: &lt;exe-dir&gt;/resources/template-previews/{num:D2}.png
    /// 파일 없으면 cache 에 null 저장 → popup 이 placeholder 표시.
    /// </summary>
    private Image? LoadPreview(int num)
    {
        if (_previewCache.TryGetValue(num, out var cached)) return cached;

        Image? img = null;
        try
        {
            var path = Path.Combine(
                AppContext.BaseDirectory, "resources", "template-previews", $"{num:D2}.png");
            if (File.Exists(path))
            {
                // FileStream 으로 읽어 즉시 메모리에 복제 — File 핸들 잡지 않음 (덮어쓰기 가능).
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                img = Image.FromStream(fs);
            }
        }
        catch { /* 손상 PNG 등 — null 로 fallback */ }
        _previewCache[num] = img;
        return img;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var img in _previewCache.Values) img?.Dispose();
            _previewCache.Clear();
        }
        base.Dispose(disposing);
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
