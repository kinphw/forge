// 탭 ③ 마크다운 입력 — md 입력 + 변환 버튼.
// Python 원본 forge/ui/tabs/markdown_tab.py 의 핵심 1:1.

using System.Text;
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
    private string _lastConvertedFile = "";

    public MarkdownTab(AppState state, Action updateStatus)
    {
        _state = state;
        _updateStatus = updateStatus;
        BuildUI();
    }

    private void BuildUI()
    {
        // SplitContainer: 좌 md 에디터 + 우 로그
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 700,
        };

        // ─── 좌측: md 에디터 ──────────────────────
        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6),
        };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // 메타 입력
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // md textbox
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));   // 버튼

        // 메타 입력 (작성부서·작성일)
        var metaPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
        };
        metaPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        metaPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metaPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        metaPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metaPanel.Controls.Add(new Label { Text = "작성부서", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _deptInput = new TextBox { Dock = DockStyle.Fill };
        metaPanel.Controls.Add(_deptInput, 1, 0);
        metaPanel.Controls.Add(new Label { Text = "작성일", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        _dateInput = new TextBox { Dock = DockStyle.Fill, Text = DateTime.Now.ToString("yyyy-MM-dd") };
        metaPanel.Controls.Add(_dateInput, 3, 0);
        leftPanel.Controls.Add(metaPanel, 0, 0);

        // md 입력
        _mdInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10),
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = true,
        };
        // 샘플 placeholder
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
        leftPanel.Controls.Add(_mdInput, 0, 1);

        // 버튼 row
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0),
        };
        var openButton = new Button { Text = "파일 열기", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        openButton.Click += OnOpenFile;
        _convertButton = new Button { Text = "변환 (md → hwpx)", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        _convertButton.Click += OnConvert;
        _saveAsButton = new Button { Text = "다른 이름으로 저장", AutoSize = true, Enabled = false };
        _saveAsButton.Click += OnSaveAs;
        btnPanel.Controls.Add(openButton);
        btnPanel.Controls.Add(_convertButton);
        btnPanel.Controls.Add(_saveAsButton);
        leftPanel.Controls.Add(btnPanel, 0, 2);

        // ─── 우측: 로그 출력 ──────────────────────
        _logOutput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(245, 245, 245),
        };

        split.Panel1.Controls.Add(leftPanel);
        split.Panel2.Controls.Add(_logOutput);
        Controls.Add(split);
    }

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
        if (FindForm() is not MainForm main) return;

        // 한/글 lazy attach
        if (!main.EnsureHwp(allowSpawn: false))
        {
            Log("[변환] 한/글 attach 실패 — 한/글 먼저 실행해 주세요.");
            return;
        }
        if (_state.Hwp is null)
        {
            Log("[변환] AppState.Hwp == null — 비정상");
            return;
        }

        var src = _mdInput.Text;
        if (string.IsNullOrWhiteSpace(src))
        {
            Log("[변환] md 입력 비어있음.");
            return;
        }

        Log("[변환] 시작 ...");
        _convertButton.Enabled = false;
        try
        {
            var doc = Parser.Parse(src);
            Log($"  파싱: {doc.Nodes.Count} 노드 (메타 보고서명={doc.Metadata.ReportTitle ?? "(없음)"})");

            HwpxWriter.LogFn logFn = msg => Log($"  {msg}");
            HwpxWriter.GenerateHwpxViaCom(
                _state.Hwp.Hwp,
                doc,
                outPath: null,  // 지연-저장 — 저장 단계 skip
                spec: _state.Spec,
                log: logFn,
                mode: HwpxWriteMode.New,
                department: _deptInput.Text,
                date: string.IsNullOrWhiteSpace(_dateInput.Text) ? null : _dateInput.Text);

            _lastConvertedFile = "";  // 아직 저장 안 함
            _saveAsButton.Enabled = true;
            Log("[변환] ✔ 완료 — 한/글 화면 확인 후 [다른 이름으로 저장] 으로 hwpx 저장");
        }
        catch (Exception ex)
        {
            Log($"[변환] ✘ 실패: {ex.GetType().Name}: {ex.Message}");
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
            _lastConvertedFile = dlg.FileName;
            Log($"[저장] {dlg.FileName}");
        }
        catch (Exception ex)
        {
            Log($"[저장] ✘ 실패: {ex.Message}");
        }
    }

    private void Log(string msg)
    {
        if (_logOutput.IsDisposed) return;
        _logOutput.AppendText(msg + Environment.NewLine);
        _logOutput.SelectionStart = _logOutput.TextLength;
        _logOutput.ScrollToCaret();
    }
}
