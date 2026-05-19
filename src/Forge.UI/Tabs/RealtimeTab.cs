// 탭 ① 실시간 작업 — 활성 한/글 문서에 룰 1개씩 적용.
// Python 원본 forge/ui/tabs/realtime_tab.py 의 핵심 포팅 (1321줄 중 단축키+룰 핵심).
//
// 구조:
//   - 단축키 letter Entry 9개 (Q/W/A/S/F/G/D/Z/X) + 사용자 변경 시 GlobalHotkeyManager.Replace
//   - 폰트 4쌍 (font_name + size) Entry — 룰 호출 시 SSOT
//   - 룰 버튼 — 단축키 없이도 직접 실행 가능
//
// 핸들러는 한/글 attach 필요 → MainForm.EnsureHwp() 통해 lazy.

using Forge.Core;
using Forge.Core.Formatter;
using Forge.Core.Renderers;
using Forge.Core.Templates;
using Forge.Win32;

namespace Forge.UI.Tabs;

public sealed class RealtimeTab : TabPage
{
    private readonly AppState _state;
    private readonly Action _updateStatus;

    // 단축키 letter Entry 9개 (Actions.All 순서)
    private readonly TextBox[] _hkLetters = new TextBox[Actions.All.Count];

    // 폰트 입력 칸 (Python var_font1~4, var_size1~4 등가)
    public string Font1Name { get; private set; } = "휴먼명조";
    public double Font1Size { get; private set; } = 15.0;
    public string Font2Name { get; private set; } = "맑은 고딕";
    public double Font2Size { get; private set; } = 12.0;
    public string Font3Name { get; private set; } = "HY헤드라인M";
    public double Font3Size { get; private set; } = 16.0;
    public string Font4Name { get; private set; } = "HY울릉도M";
    public double Font4Size { get; private set; } = 15.0;
    public double BlankSize { get; private set; } = 8.0;

    private TextBox _font1Name = null!, _font2Name = null!, _font3Name = null!, _font4Name = null!;
    private TextBox _font1Size = null!, _font2Size = null!, _font3Size = null!, _font4Size = null!;
    private TextBox _blankSize = null!;
    private TextBox _logOutput = null!;

    private GlobalHotkeyManager? _hotkeys;

    public RealtimeTab(AppState state, Action updateStatus)
    {
        _state = state;
        _updateStatus = updateStatus;
        BuildUI();
        // hotkey 등록은 MainForm 의 OnShown 에서 호출 (handle 만들어진 뒤)
    }

    private MainForm? Main => FindForm() as MainForm;

    // ──────────────────────────────────────────────────────────────────
    // UI
    // ──────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 580,
        };

        // 좌측 패널 — 단축키 + 폰트 + 룰 버튼
        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(8),
        };
        leftPanel.Controls.Add(BuildHotkeySection());
        leftPanel.Controls.Add(BuildFontSection());
        leftPanel.Controls.Add(BuildRuleButtonSection());

        // 우측 — 로그
        _logOutput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new System.Drawing.Font("Consolas", 9),
            BackColor = System.Drawing.Color.FromArgb(245, 245, 245),
        };

        split.Panel1.Controls.Add(leftPanel);
        split.Panel2.Controls.Add(_logOutput);
        Controls.Add(split);
    }

    private GroupBox BuildHotkeySection()
    {
        var box = new GroupBox
        {
            Text = "단축키 (Ctrl+Shift+<key>) — letter 변경 가능",
            Dock = DockStyle.Top,
            Height = 280,
            Padding = new Padding(8),
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = Actions.All.Count,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var saved = UserSettings.GetKeymap();
        for (int i = 0; i < Actions.All.Count; i++)
        {
            var act = Actions.All[i];
            var letter = saved.TryGetValue(act.Id, out var k) && k is not null ? k : act.DefaultKey;
            var letterBox = new TextBox
            {
                Text = letter,
                MaxLength = 1,
                Width = 40,
                TextAlign = HorizontalAlignment.Center,
            };
            int hkId = i + 1;
            letterBox.Leave += (_, _) => OnHotkeyLetterChanged(hkId, letterBox);
            _hkLetters[i] = letterBox;
            grid.Controls.Add(letterBox, 0, i);

            var label = new Label { Text = act.Label, AutoSize = true, Anchor = AnchorStyles.Left };
            grid.Controls.Add(label, 1, i);

            var btn = new Button { Text = "▶ 실행", AutoSize = true };
            btn.Click += (_, _) => SafeInvoke(act);
            grid.Controls.Add(btn, 2, i);
        }
        box.Controls.Add(grid);
        return box;
    }

    private GroupBox BuildFontSection()
    {
        var box = new GroupBox
        {
            Text = "폰트 입력 (룰 호출 시 SSOT)",
            Dock = DockStyle.Top,
            Height = 200,
            Padding = new Padding(8),
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 5,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        TextBox AddRow(int row, string label, string defaultName, double defaultSize)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            var nameBox = new TextBox { Dock = DockStyle.Fill, Text = defaultName };
            grid.Controls.Add(nameBox, 1, row);
            grid.Controls.Add(new Label { Text = "크기", AutoSize = true, Anchor = AnchorStyles.Right }, 2, row);
            var sizeBox = new TextBox { Dock = DockStyle.Fill, Text = defaultSize.ToString("0.#") };
            grid.Controls.Add(sizeBox, 3, row);
            // 변경 시 캐시 갱신
            nameBox.TextChanged += (_, _) => SyncFontFields();
            sizeBox.TextChanged += (_, _) => SyncFontFields();
            return nameBox;
        }

        _font1Name = AddRow(0, "본문 (A)", Font1Name, Font1Size);
        _font1Size = (TextBox)grid.GetControlFromPosition(3, 0)!;
        _font2Name = AddRow(1, "주석 (S)", Font2Name, Font2Size);
        _font2Size = (TextBox)grid.GetControlFromPosition(3, 1)!;
        _font3Name = AddRow(2, "헤드라인 (F)", Font3Name, Font3Size);
        _font3Size = (TextBox)grid.GetControlFromPosition(3, 2)!;
        _font4Name = AddRow(3, "울릉도 (G)", Font4Name, Font4Size);
        _font4Size = (TextBox)grid.GetControlFromPosition(3, 3)!;

        // blank size
        grid.Controls.Add(new Label { Text = "빈줄 크기 (D)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        _blankSize = new TextBox { Dock = DockStyle.Fill, Text = BlankSize.ToString("0.#") };
        _blankSize.TextChanged += (_, _) => SyncFontFields();
        grid.Controls.Add(_blankSize, 1, 4);

        box.Controls.Add(grid);
        return box;
    }

    private GroupBox BuildRuleButtonSection()
    {
        var box = new GroupBox
        {
            Text = "개별 룰 실행 (단축키 없이)",
            Dock = DockStyle.Top,
            Height = 80,
            Padding = new Padding(8),
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
        };
        foreach (var act in Actions.All)
        {
            var btn = new Button
            {
                Text = $"{act.Label}",
                AutoSize = true,
                Margin = new Padding(2),
            };
            btn.Click += (_, _) => SafeInvoke(act);
            flow.Controls.Add(btn);
        }
        box.Controls.Add(flow);
        return box;
    }

    private void SyncFontFields()
    {
        Font1Name = _font1Name.Text;
        Font2Name = _font2Name.Text;
        Font3Name = _font3Name.Text;
        Font4Name = _font4Name.Text;
        if (double.TryParse(_font1Size.Text, out var s1)) Font1Size = s1;
        if (double.TryParse(_font2Size.Text, out var s2)) Font2Size = s2;
        if (double.TryParse(_font3Size.Text, out var s3)) Font3Size = s3;
        if (double.TryParse(_font4Size.Text, out var s4)) Font4Size = s4;
        if (double.TryParse(_blankSize.Text, out var sb)) BlankSize = sb;
    }

    private void OnHotkeyLetterChanged(int hkId, TextBox letterBox)
    {
        var letter = letterBox.Text.Trim();
        var act = Actions.ByHkId(hkId);
        if (letter.Length == 1 && (char.IsLetter(letter[0]) || char.IsDigit(letter[0])))
        {
            letterBox.Text = letter.ToUpperInvariant();
            _hotkeys?.Replace(hkId, VirtualKey.Letter(letter[0]), $"Ctrl+Shift+{letter.ToUpperInvariant()}");
            UserSettings.SetKeymapEntry(act.Id, letter);
            Log($"  [hotkey] {act.Label} → Ctrl+Shift+{letter.ToUpperInvariant()}");
        }
        else
        {
            // 비활성화 — letterBox 비우면 hotkey 등록 안 함
            letterBox.Text = "";
            _hotkeys?.Replace(hkId, null, $"{act.Label} (비활성)");
            UserSettings.SetKeymapEntry(act.Id, null);
            Log($"  [hotkey] {act.Label} 비활성화");
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // GlobalHotkeyManager 시작 — MainForm.OnShown 시점에 호출
    // ──────────────────────────────────────────────────────────────────

    public void StartHotkeys()
    {
        if (_hotkeys is not null) return;
        var main = Main;
        if (main is null) return;

        _hotkeys = new GlobalHotkeyManager(main);
        const uint MOD_CS = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT;
        for (int i = 0; i < Actions.All.Count; i++)
        {
            var act = Actions.All[i];
            var letter = _hkLetters[i].Text;
            uint? vk = letter.Length == 1 ? VirtualKey.Letter(letter[0]) : null;
            int hkId = i + 1;
            _hotkeys.Add(hkId, MOD_CS, vk, () => SafeInvoke(act),
                vk is null ? $"{act.Label} (비활성)" : $"Ctrl+Shift+{letter}");
        }
        var results = _hotkeys.Start();
        foreach (var (label, ok) in results)
            Log(ok ? $"  [hotkey] ✔ {label}" : $"  [hotkey] ✘ {label} (다른 앱이 사용 중)");
    }

    public void StopHotkeys() => _hotkeys?.Dispose();

    // ──────────────────────────────────────────────────────────────────
    // 핸들러 (Actions 가 호출)
    // ──────────────────────────────────────────────────────────────────

    private void SafeInvoke(ActionDef act)
    {
        if (Main is null) return;
        if (!Main.EnsureHwp(allowSpawn: false))
        {
            Log($"[{act.Label}] 한/글 attach 실패");
            return;
        }
        try
        {
            Log($"[{act.Label}] 실행");
            act.Invoke(this);
            Log($"[{act.Label}] ✔ 완료");
        }
        catch (Exception ex)
        {
            Log($"[{act.Label}] ✘ {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void RunAutoAlign()
    {
        if (_state.Hwp is null) return;
        Forge.Core.Linter.LogFn lg = msg => Log("  " + msg);
        Forge.Core.Linter.IndentAlign.AlignCurrentParagraph(_state.Hwp.Hwp, lg);
        Forge.Core.Linter.Kerning.AdjustKerningCurrentParagraph(_state.Hwp.Hwp, lg);
        Forge.Core.Linter.IndentAlign.AlignCurrentParagraph(_state.Hwp.Hwp, lg);
    }

    public void RunWordPull()
    {
        if (_state.Hwp is null) return;
        Forge.Core.Linter.LogFn lg = msg => Log("  " + msg);
        Forge.Core.Linter.Squeeze.FitCurrentParagraphToOneLine(_state.Hwp.Hwp, log: lg);
    }

    public void RunKerningReset()
    {
        if (_state.Hwp is null) return;
        // selection 영역의 자간 0 reset
        Primitives.ResetKerningZero(_state.Hwp.Hwp);
    }

    public void RunParagraphSize8()
    {
        if (_state.Hwp is null) return;
        // 현재 문단 글자크기 = BlankSize (빈줄 자간 꼬임 회피용 작은 크기)
        dynamic hwp = _state.Hwp.Hwp;
        hwp.HAction.Run("MoveParaBegin");
        hwp.HAction.Run("MoveSelParaEnd");
        Primitives.SetFontSize(hwp, BlankSize);
        hwp.HAction.Run("Cancel");
    }

    public void ApplyFont(string fontName, double size)
    {
        if (_state.Hwp is null) return;
        Primitives.SetFont(_state.Hwp.Hwp, fontName, size);
    }

    public void RunMdConvertSelection()
    {
        if (_state.Hwp is null) return;
        try
        {
            HwpxWriter.LogFn lg = msg => Log("  " + msg);
            var count = HwpxWriter.ConvertSelectionToHwpx(_state.Hwp.Hwp, _state.Spec, lg);
            Log($"  → {count} 노드 변환");
        }
        catch (NoSelectionException ex)
        {
            MessageBox.Show(FindForm(), ex.Message, "선택 영역 변환", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void Log(string msg)
    {
        if (_logOutput.IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => Log(msg))); return; }
        _logOutput.AppendText(msg + Environment.NewLine);
        _logOutput.SelectionStart = _logOutput.TextLength;
        _logOutput.ScrollToCaret();
    }
}
