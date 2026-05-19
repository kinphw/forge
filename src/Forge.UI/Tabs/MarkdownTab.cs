// 탭 ③ 마크다운 입력 — md 입력 + 변환 + 저장.
// Python 원본 forge/ui/tabs/markdown_tab.py 핵심 1:1 + ForgeTheme styling.

using Forge.Core;
using Forge.Core.Formatter;

namespace Forge.UI.Tabs;

public sealed class MarkdownTab : TabPage
{
    private readonly AppState _state;
    private readonly Action _updateStatus;

    private TextBox _mdInput = null!;
    private TextBox _deptInput = null!;
    private TextBox _dateInput = null!;
    private TextBox _logOutput = null!;
    private Button _convertButton = null!;
    private Button _saveAsButton = null!;
    private Label _statusLine = null!;

    public MarkdownTab(AppState state, Action updateStatus)
    {
        _state = state;
        _updateStatus = updateStatus;
        BackColor = ForgeTheme.Background;
        Padding = ForgeTheme.PanelPadding;
        BuildUI();
    }

    private MainForm? Main => FindForm() as MainForm;

    private void BuildUI()
    {
        // 1행 2열 TableLayoutPanel — 좌:우 50:50
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = ForgeTheme.Background,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildLeftPanel(), 0, 0);
        root.Controls.Add(BuildRightPanel(), 1, 0);
    }

    private Control BuildLeftPanel()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(0, 0, ForgeTheme.Pad, 0),
            BackColor = ForgeTheme.Background,
        };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // 헤더
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // 설명
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // 메타 (작성부서/일자)
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // md 입력
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // 버튼 row

        grid.Controls.Add(ForgeTheme.SectionTitle("마크다운 입력 → 새 hwpx 변환"), 0, 0);
        grid.Controls.Add(ForgeTheme.SectionDesc(
            "개조식 markdown 을 한/글에 새 문서로 변환. front-matter 의 보고서명 + 아래 작성부서·일자로 헤더 자동 생성."),
            0, 1);

        // 메타 입력 (작성부서·작성일)
        var metaBox = new GroupBox
        {
            Text = "메타 입력",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, ForgeTheme.Pad),
        };
        ForgeTheme.StyleGroup(metaBox);
        var metaGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 1,
            AutoSize = true,
            Padding = new Padding(4),
        };
        metaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        metaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        metaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        metaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        metaGrid.Controls.Add(MakeFieldLabel("작성부서"), 0, 0);
        _deptInput = new TextBox { Dock = DockStyle.Fill, Font = ForgeTheme.Body() };
        ForgeTheme.StyleInput(_deptInput);
        metaGrid.Controls.Add(_deptInput, 1, 0);
        metaGrid.Controls.Add(MakeFieldLabel("작성일"), 2, 0);
        _dateInput = new TextBox { Dock = DockStyle.Fill, Text = DateTime.Now.ToString("yyyy-MM-dd"), Font = ForgeTheme.Body() };
        ForgeTheme.StyleInput(_dateInput);
        metaGrid.Controls.Add(_dateInput, 3, 0);
        metaBox.Controls.Add(metaGrid);
        grid.Controls.Add(metaBox, 0, 2);

        // md 입력
        var mdBox = new GroupBox
        {
            Text = "Markdown 원본",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, ForgeTheme.Pad),
        };
        ForgeTheme.StyleGroup(mdBox);
        _mdInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = ForgeTheme.Mono(),
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = true,
            BorderStyle = BorderStyle.None,
            BackColor = ForgeTheme.PanelBg,
            ForeColor = ForgeTheme.TextPrimary,
        };
        _mdInput.Text =
            "---" + Environment.NewLine +
            "보고서명: 새 보고서" + Environment.NewLine +
            "---" + Environment.NewLine +
            "" + Environment.NewLine +
            "1. 개요" + Environment.NewLine +
            "" + Environment.NewLine +
            "가. 배경" + Environment.NewLine +
            "" + Environment.NewLine +
            "□ 본문 내용" + Environment.NewLine;
        mdBox.Controls.Add(_mdInput);
        grid.Controls.Add(mdBox, 0, 3);

        // 버튼 row
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(0, ForgeTheme.Pad, 0, 0),
        };
        var openButton = new Button { Text = "📂 파일 열기" };
        ForgeTheme.StyleFlatButton(openButton);
        openButton.Click += OnOpenFile;
        openButton.Margin = new Padding(0, 0, 8, 0);

        _convertButton = new Button { Text = "🔄 변환 (md → hwpx)" };
        ForgeTheme.StyleFlatButton(_convertButton, accent: true);
        _convertButton.Click += OnConvert;
        _convertButton.Margin = new Padding(0, 0, 8, 0);

        _saveAsButton = new Button { Text = "💾 다른 이름으로 저장", Enabled = false };
        ForgeTheme.StyleFlatButton(_saveAsButton);
        _saveAsButton.Click += OnSaveAs;

        btnPanel.Controls.Add(openButton);
        btnPanel.Controls.Add(_convertButton);
        btnPanel.Controls.Add(_saveAsButton);
        grid.Controls.Add(btnPanel, 0, 4);

        return grid;
    }

    private Control BuildRightPanel()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(ForgeTheme.Pad, 0, 0, 0),
            BackColor = ForgeTheme.Background,
        };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(ForgeTheme.SectionTitle("변환 로그"), 0, 0);
        _statusLine = new Label
        {
            Text = "변환 대기 중",
            Font = ForgeTheme.Small(),
            ForeColor = ForgeTheme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, ForgeTheme.Pad),
        };
        grid.Controls.Add(_statusLine, 0, 1);

        _logOutput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = ForgeTheme.Mono(),
            BackColor = ForgeTheme.LogBg,
            ForeColor = ForgeTheme.LogText,
            BorderStyle = BorderStyle.FixedSingle,
        };
        grid.Controls.Add(_logOutput, 0, 2);
        return grid;
    }

    private static Label MakeFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = ForgeTheme.BodyBold(),
        ForeColor = ForgeTheme.TextPrimary,
        Margin = new Padding(0, 6, 0, 0),
    };

    private void OnOpenFile(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Markdown (*.md)|*.md|모든 파일 (*.*)|*.*",
            Title = "md 파일 열기",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            _mdInput.Text = File.ReadAllText(dlg.FileName);
            Log($"[열기] {dlg.FileName} ({new FileInfo(dlg.FileName).Length} bytes)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), $"파일 읽기 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnConvert(object? sender, EventArgs e)
    {
        if (Main is null) return;

        if (!Main.EnsureHwp(allowSpawn: false))
        {
            Log("[변환] 한/글 attach 실패 — 한/글 먼저 실행해 주세요.");
            return;
        }
        if (_state.Hwp is null) return;

        var src = _mdInput.Text;
        if (string.IsNullOrWhiteSpace(src))
        {
            Log("[변환] md 입력 비어있음.");
            return;
        }

        Log("[변환] 시작 ...");
        SetStatus("변환 중 ...", ForgeTheme.Warning);
        _convertButton.Enabled = false;
        try
        {
            var doc = Parser.Parse(src);
            Log($"  파싱: {doc.Nodes.Count} 노드 (메타 보고서명={doc.Metadata.ReportTitle ?? "(없음)"})");

            HwpxWriter.LogFn logFn = msg => Log($"  {msg}");
            HwpxWriter.GenerateHwpxViaCom(
                _state.Hwp.Hwp,
                doc,
                outPath: null,
                spec: _state.Spec,
                log: logFn,
                mode: HwpxWriteMode.New,
                department: _deptInput.Text,
                date: string.IsNullOrWhiteSpace(_dateInput.Text) ? null : _dateInput.Text);

            _saveAsButton.Enabled = true;
            Log("[변환] ✔ 완료 — 한/글 화면 확인 후 [다른 이름으로 저장] 으로 hwpx 저장");
            SetStatus("✔ 변환 완료 — 저장 대기", ForgeTheme.Success);
        }
        catch (Exception ex)
        {
            Log($"[변환] ✘ 실패: {ex.GetType().Name}: {ex.Message}");
            SetStatus("✘ 변환 실패", ForgeTheme.Error);
            MessageBox.Show(FindForm(), $"변환 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _convertButton.Enabled = true;
        }
    }

    private void OnSaveAs(object? sender, EventArgs e)
    {
        if (_state.Hwp is null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "한/글 (*.hwpx)|*.hwpx",
            Title = "hwpx 저장",
            FileName = "report.hwpx",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            HwpxWriter.SaveAsHwpx(_state.Hwp.Hwp, dlg.FileName);
            Log($"[저장] {dlg.FileName}");
            SetStatus($"💾 저장됨: {Path.GetFileName(dlg.FileName)}", ForgeTheme.Success);
        }
        catch (Exception ex)
        {
            Log($"[저장] ✘ 실패: {ex.Message}");
            SetStatus("✘ 저장 실패", ForgeTheme.Error);
        }
    }

    private void SetStatus(string text, Color color)
    {
        _statusLine.Text = text;
        _statusLine.ForeColor = color;
    }

    private void Log(string msg)
    {
        if (_logOutput is null || _logOutput.IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => Log(msg))); return; }
        _logOutput.AppendText(msg + Environment.NewLine);
        _logOutput.SelectionStart = _logOutput.TextLength;
        _logOutput.ScrollToCaret();
    }
}
