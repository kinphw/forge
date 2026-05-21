// 탭 ① 실시간 작업 — Python realtime_tab.py 충실 포팅.
//
// 구조:
//   상단 meta bar — 🧹 로그 비우기 + 개별기능 표시 체크박스
//   3-컬럼 grid [버튼 | 폰트cluster | 단축키]:
//     그룹 1 (정렬): 자동 정렬 (Q) / 어절 끌어올림 (W)
//     ───────────────────────────────────────
//     그룹 2 (폰트, A/S/D/F/G 키보드 순):
//       본문 (A) / 주석 (S) / 빈줄 크기 (D) / 헤드라인 (F) / 울릉도 (G)
//     ───────────────────────────────────────
//     그룹 3 (기타): 자간 0 (Z) / 선택→md 변환 (X)
//
// 영속화: 폰트·크기·blank·hotkey letter 변경 → UserSettings JSON 자동 저장 (debounce).

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
    private readonly ToolTip _tooltip = new()
    {
        AutoPopDelay = 12000,
        InitialDelay = 400,
        ReshowDelay = 100,
        ShowAlways = true,
    };

    // ─ 사용자 입력 상태 (Python var_font1~4, var_size1~4 등가) ───────
    public string Font1Name { get; private set; } = "휴먼명조";
    public double Font1Size { get; private set; } = 15.0;
    public string Font2Name { get; private set; } = "맑은 고딕";
    public double Font2Size { get; private set; } = 12.0;
    public string Font3Name { get; private set; } = "HY헤드라인M";
    public double Font3Size { get; private set; } = 15.0;
    public string Font4Name { get; private set; } = "HY울릉도M";
    public double Font4Size { get; private set; } = 15.0;
    public double BlankSize { get; private set; } = 8.0;

    private CheckBox _showIndividual = null!;
    private readonly TextBox[] _hkLetters = new TextBox[Actions.All.Count];
    private readonly Label[] _hkStatusLbls = new Label[Actions.All.Count];

    private TextBox _logOutput = null!;
    private GlobalHotkeyManager? _hotkeys;
    private System.Windows.Forms.Timer? _persistTimer;
    private readonly Dictionary<string, object?> _pendingPersist = new();

    public RealtimeTab(AppState state, Action updateStatus)
    {
        _state = state;
        _updateStatus = updateStatus;
        BackColor = ForgeTheme.Background;
        Padding = ForgeTheme.PanelPadding;
        LoadFromSettings();
        BuildUI();
    }

    private MainForm? Main => FindForm() as MainForm;

    // ──────────────────────────────────────────────────────────────────
    // 설정 영속화
    // ──────────────────────────────────────────────────────────────────

    private void LoadFromSettings()
    {
        var rt = UserSettings.GetSection("realtime");
        Font1Name = GetStr(rt, "font1", Font1Name);
        Font2Name = GetStr(rt, "font2", Font2Name);
        Font3Name = GetStr(rt, "font3", Font3Name);
        Font4Name = GetStr(rt, "font4", Font4Name);
        Font1Size = GetDouble(rt, "size1", Font1Size);
        Font2Size = GetDouble(rt, "size2", Font2Size);
        Font3Size = GetDouble(rt, "size3", Font3Size);
        Font4Size = GetDouble(rt, "size4", Font4Size);
        BlankSize = GetDouble(rt, "blank_size", BlankSize);
    }

    private static string GetStr(Dictionary<string, System.Text.Json.JsonElement> d, string key, string fallback) =>
        d.TryGetValue(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;

    private static double GetDouble(Dictionary<string, System.Text.Json.JsonElement> d, string key, double fallback)
    {
        if (!d.TryGetValue(key, out var v)) return fallback;
        if (v.ValueKind == System.Text.Json.JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(v.GetString(), out var p)) return p;
        return fallback;
    }

    private void QueuePersist(string key, object? value)
    {
        _pendingPersist[key] = value;
        _persistTimer ??= new System.Windows.Forms.Timer { Interval = 500 };
        _persistTimer.Tick -= OnPersistFlush;
        _persistTimer.Tick += OnPersistFlush;
        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void OnPersistFlush(object? sender, EventArgs e)
    {
        _persistTimer?.Stop();
        if (_pendingPersist.Count == 0) return;
        UserSettings.UpdateSection("realtime", new Dictionary<string, object?>(_pendingPersist));
        _pendingPersist.Clear();
    }

    /// <summary>외부에서 debounce timer 잔량을 즉시 flush.
    /// MainForm 의 종료 시 / 💾 설정 저장 버튼이 호출.</summary>
    public void FlushPersist() => OnPersistFlush(null, EventArgs.Empty);

    /// <summary>
    /// realtime 탭의 4 폰트 cluster + BlankSize 를 spec 에 주입한 새 ReportSpec 반환.
    /// Python apply_overrides_to_spec 1:1 — 'realtime_tab 이 폰트 SSOT, 마크다운 변환은 따라감'.
    ///
    /// 매핑 규칙:
    ///   Font1/Size1 (본문)    → bullets[*].Font·SizePt + Conclusion(Font/SizePt) + DateFont
    ///   Font2/Size2 (주석)    → Annotation(Font/SizePt)
    ///   Font3       (헤드라인) → Title/SectionTitle/Subsection/NoteHeader Font (face only)
    ///   Font4       (울릉도)  → BulletSummaryFont
    ///   BlankSize             → BlankParaPt
    /// </summary>
    public ReportSpec ApplyOverridesToSpec(ReportSpec spec)
    {
        var newBullets = spec.Bullets
            .Select(b => b with { Font = Font1Name, SizePt = Font1Size })
            .ToArray();
        var newAnnotation = spec.Annotation with { Font = Font2Name, SizePt = Font2Size };
        return spec with
        {
            Bullets = newBullets,
            Annotation = newAnnotation,
            ConclusionFont = Font1Name,
            ConclusionSizePt = Font1Size,
            DateFont = Font1Name,           // size 는 DateSizePt 그대로
            TitleFont = Font3Name,
            SectionTitleFont = Font3Name,
            SubsectionFont = Font3Name,
            NoteHeaderFont = Font3Name,
            BulletSummaryFont = Font4Name,
            BlankParaPt = BlankSize,
        };
    }

    // ──────────────────────────────────────────────────────────────────
    // UI 구축
    // ──────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // TableLayoutPanel 1행 2열 — 좌:우 65:35 자동 비율 (SplitContainer 의 SplitterDistance
        // 초기화 타이밍 이슈 회피).
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = ForgeTheme.Background,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        // ─ 좌측 = 컨트롤 (스크롤) ─
        // FlowLayoutPanel + AutoSize 가 자식 폭을 부모에 안 맞춰 GroupBox 가 좁아지는 issue 회피.
        // Dock=Top 패턴 — 자식들이 부모(Panel) 폭 자동 채움. 추가 순서는 bottom-up.
        var leftScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = ForgeTheme.Background,
            Padding = new Padding(0, 0, ForgeTheme.Pad, 0),
        };
        root.Controls.Add(leftScroll, 0, 0);

        // Dock=Top stack — 마지막 추가가 맨 위로 (역순 추가)
        var mainGroup = BuildMainGroup();
        mainGroup.Dock = DockStyle.Top;

        var metaBar = BuildMetaBar();
        metaBar.Dock = DockStyle.Top;

        var desc = ForgeTheme.SectionDesc(
            "활성 한/글 문서에 룰을 1개씩 적용. 결과는 우측 로그에 누적.\n" +
            "단축키 letter 는 자유 변경 (Ctrl+Shift+ 조합 유지).");
        desc.Dock = DockStyle.Top;

        var title = ForgeTheme.SectionTitle("개별 작업 — 실시간 모드");
        title.Dock = DockStyle.Top;

        // 추가 순서 = 아래에서 위 (Dock=Top stacks 의 z-order)
        leftScroll.Controls.Add(mainGroup);
        leftScroll.Controls.Add(metaBar);
        leftScroll.Controls.Add(desc);
        leftScroll.Controls.Add(title);

        // ─ 우측 = 로그 ─
        _logOutput = new TextBox
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
        root.Controls.Add(_logOutput, 1, 0);
    }

    private Panel BuildMetaBar()
    {
        var bar = new Panel
        {
            AutoSize = false,
            Height = 44,
            Margin = new Padding(0, 0, 0, ForgeTheme.Pad),
        };
        var clearBtn = new Button { Text = "로그 비우기" };
        ForgeTheme.StyleFlatButton(clearBtn, glyph: MdlIcon.Clear);
        clearBtn.Click += (_, _) => _logOutput.Clear();
        clearBtn.Location = new Point(0, 4);
        bar.Controls.Add(clearBtn);
        _tooltip.SetToolTip(clearBtn, "로그 영역의 모든 내용 지우기");

        var rt = UserSettings.GetSection("realtime");
        var showInitial = rt.TryGetValue("show_individual", out var v) &&
                          v.ValueKind == System.Text.Json.JsonValueKind.True;
        _showIndividual = new CheckBox
        {
            Text = "개별기능 표시",
            AutoSize = true,
            Font = ForgeTheme.Body(),
            Checked = showInitial,
            Location = new Point(160, 8),
        };
        _showIndividual.CheckedChanged += (_, _) =>
        {
            QueuePersist("show_individual", _showIndividual.Checked);
            // TODO: 행 11·12 (들여쓰기 정렬·자간조정 개별 버튼) 표시 토글
        };
        bar.Controls.Add(_showIndividual);

        var resetBtn = new Button { Text = "설정 초기화" };
        ForgeTheme.StyleFlatButton(resetBtn, glyph: MdlIcon.Refresh);
        resetBtn.Click += (_, _) => OnResetSettings();
        resetBtn.Location = new Point(280, 4);
        bar.Controls.Add(resetBtn);
        _tooltip.SetToolTip(resetBtn,
            "폰트·크기·단축키를 모두 default 로 복원.\n" +
            "한/글 적용 동작은 즉시 default. UI 입력 칸은 다음 앱 시작 시 반영.");

        bar.Width = 480;
        bar.Height = 36;
        return bar;
    }

    /// <summary>
    /// 폰트(4) + 빈줄 크기 + 단축키 letter 8개를 default 로 복원.
    /// 메모리 상태 + UserSettings 의 realtime·keymap 섹션 즉시 reset.
    /// UI 입력 칸은 컨트롤 ref 추적 비용 회피 위해 재시작 시 반영.
    /// </summary>
    private void OnResetSettings()
    {
        var dr = MessageBox.Show(FindForm(),
            "RealtimeTab 의 폰트·크기·단축키를 모두 default 로 초기화합니다.\n\n" +
            "• 한/글 적용 동작(hotkey, 마크다운 변환의 SSOT 주입) 은 즉시 default 로 복원됩니다.\n" +
            "• UI 입력 칸 (폰트 콤보·크기·단축키 letter) 표시는 다음 앱 시작 시 반영됩니다.\n\n" +
            "계속하시겠습니까?",
            "설정 초기화",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (dr != DialogResult.OK) return;

        Font1Name = "휴먼명조";    Font1Size = 15.0;
        Font2Name = "맑은 고딕";    Font2Size = 12.0;
        Font3Name = "HY헤드라인M"; Font3Size = 15.0;
        Font4Name = "HY울릉도M";   Font4Size = 15.0;
        BlankSize = 8.0;

        // pending debounce 폐기 — reset 직전의 미반영 변경이 살아남는 사고 방지
        _pendingPersist.Clear();
        _persistTimer?.Stop();

        var okRt = UserSettings.RemoveSection("realtime");
        var okKm = UserSettings.RemoveSection("keymap");

        Log($"✔ 설정 초기화 완료 (realtime={(okRt ? "OK" : "FAIL")}, keymap={(okKm ? "OK" : "FAIL")}).");
        Log("  메모리 상태는 즉시 default 로 복원됨 — 다음 hotkey/변환부터 default 적용.");
        Log("  UI 입력 칸은 다음 앱 시작 시 default 로 표시됩니다.");
        _updateStatus();
    }

    private GroupBox BuildMainGroup()
    {
        // Dock=Top + AutoSize=false + Height 명시 패턴
        // (AutoSize 가 부모(leftScroll) 폭에 자동 맞춰주지 못해 column 줄어드는 사고 회피)
        var box = new GroupBox
        {
            Text = "현재 캐럿 또는 선택영역에 적용",
            AutoSize = false,
            Height = 460,  // 11 rows × ~36 + separator × 2 + header
            MinimumSize = new Size(640, 0),  // column 합 + 여유 — 좁아지면 horizontal scroll
            Margin = new Padding(0, 0, 0, ForgeTheme.Pad),
        };
        ForgeTheme.StyleGroup(box);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(0, 4, 0, 4),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));  // 버튼
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));  // 폰트 cluster
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // 단축키 (나머지)

        // 행 0: 자동 정렬 (Q)
        AddRow(grid, 0, "자동 정렬 (들·자·들)", null, null,
            hkIndex: 0,
            tooltip: "문서기호 이후 들여쓰기 조정 + 어절 잘리지 않게 자간조정 동시 실행");

        // 행 1: 어절 끌어올림 (W)
        AddRow(grid, 1, "어절 1개 끌어올림 (자간)", null, null,
            hkIndex: 1,
            tooltip: "1개 어절이 다음 줄에 튀어나온 경우 자간을 좁혀서 위로 올리기");

        // 행 2: separator
        AddSeparator(grid, 2);

        // 행 3: 본문 폰트 (A)
        AddRow(grid, 3, "폰트·크기 (본문)",
            BuildFontCluster(Font1Name, Font1Size, (n, s) => { Font1Name = n; Font1Size = s; QueuePersist("font1", n); QueuePersist("size1", s); }),
            null, hkIndex: 2,
            tooltip: "선택영역 폰트·크기 (본문) — 우측 입력값 적용");

        // 행 4: 주석 폰트 (S)
        AddRow(grid, 4, "폰트·크기 (주석)",
            BuildFontCluster(Font2Name, Font2Size, (n, s) => { Font2Name = n; Font2Size = s; QueuePersist("font2", n); QueuePersist("size2", s); }),
            null, hkIndex: 3,
            tooltip: "선택영역 폰트·크기 (주석) — 우측 입력값 적용");

        // 행 5: 빈줄용 크기 (D, A/S/D/F/G 순)
        AddRow(grid, 5, "현재 문단 → 글자크기",
            BuildSizeOnlyCluster(BlankSize, s => { BlankSize = s; QueuePersist("blank_size", s); }),
            null, hkIndex: 6,
            tooltip: "빈줄 용 글자크기 설정 (자간 꼬임 회피)");

        // 행 6: 헤드라인 폰트 (F)
        AddRow(grid, 6, "폰트·크기 (헤드라인)",
            BuildFontCluster(Font3Name, Font3Size, (n, s) => { Font3Name = n; Font3Size = s; QueuePersist("font3", n); QueuePersist("size3", s); }),
            null, hkIndex: 4,
            tooltip: "선택영역 폰트·크기 (헤드라인) — 우측 입력값 적용");

        // 행 7: 울릉도 폰트 (G)
        AddRow(grid, 7, "폰트·크기 (울릉도)",
            BuildFontCluster(Font4Name, Font4Size, (n, s) => { Font4Name = n; Font4Size = s; QueuePersist("font4", n); QueuePersist("size4", s); }),
            null, hkIndex: 5,
            tooltip: "선택영역 폰트·크기 (울릉도) — 우측 입력값 적용");

        // 행 8: separator
        AddSeparator(grid, 8);

        // 행 9: 자간 0 (Z)
        AddRow(grid, 9, "자간 0 초기화", null, null, hkIndex: 7,
            tooltip: "선택영역 자간 0% 으로 reset");

        // 행 10: 선택→md (X)
        AddRow(grid, 10, "선택영역 → 마크다운 변환", null, null, hkIndex: 8,
            tooltip: "선택 영역의 plain 텍스트를 md 로 해석해 그 자리 변환");

        box.Controls.Add(grid);
        return box;
    }

    private void AddRow(TableLayoutPanel grid, int row, string buttonText, Control? center, Control? right, int hkIndex, string tooltip)
    {
        var act = Actions.All[hkIndex];
        var btn = new Button { Text = buttonText, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(210, 32), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 8, 0) };
        ForgeTheme.StyleFlatButton(btn);
        btn.Click += (_, _) => SafeInvoke(act);
        _tooltip.SetToolTip(btn, tooltip);
        grid.Controls.Add(btn, 0, row);

        if (center is not null)
            grid.Controls.Add(center, 1, row);
        else
            grid.Controls.Add(new Label { AutoSize = true, Text = "" }, 1, row);

        grid.Controls.Add(BuildHotkeyWidget(hkIndex), 2, row);
    }

    private void AddSeparator(TableLayoutPanel grid, int row)
    {
        var sep = new Panel { Height = 1, BackColor = ForgeTheme.Border, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6) };
        grid.Controls.Add(sep, 0, row);
        grid.SetColumnSpan(sep, 3);
    }

    /// <summary>폰트 cluster — [ComboBox: 폰트명] [TextBox: pt]</summary>
    private Control BuildFontCluster(string initName, double initSize, Action<string, double> onChange)
    {
        var p = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));

        var combo = new ComboBox
        {
            Text = initName,
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = 196,
            Font = ForgeTheme.Body(),
        };
        // 설치된 폰트 자동 채우기 (top 50)
        try
        {
            using var fc = new System.Drawing.Text.InstalledFontCollection();
            foreach (var f in fc.Families.Take(120)) combo.Items.Add(f.Name);
        }
        catch { /* skip */ }

        var size = new TextBox { Text = initSize.ToString("0.#"), Width = 56, Font = ForgeTheme.Body() };
        ForgeTheme.StyleInput(size);

        void Push()
        {
            if (double.TryParse(size.Text, out var s))
                onChange(combo.Text, s);
        }
        combo.TextChanged += (_, _) => Push();
        size.TextChanged += (_, _) => Push();

        p.Controls.Add(combo, 0, 0);
        p.Controls.Add(size, 1, 0);
        return p;
    }

    /// <summary>크기-only cluster — [TextBox: pt] (행 5 빈줄용).</summary>
    private Control BuildSizeOnlyCluster(double initSize, Action<double> onChange)
    {
        var p = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
        var size = new TextBox { Text = initSize.ToString("0.#"), Width = 60, Font = ForgeTheme.Body() };
        ForgeTheme.StyleInput(size);
        size.TextChanged += (_, _) => { if (double.TryParse(size.Text, out var s)) onChange(s); };
        var pt = new Label { Text = "pt", AutoSize = true, ForeColor = ForgeTheme.TextMuted, Margin = new Padding(4, 6, 0, 0), Font = ForgeTheme.Small() };
        p.Controls.Add(size);
        p.Controls.Add(pt);
        return p;
    }

    /// <summary>단축키 widget — [TextBox: letter] [Label: ✓/✗/—].</summary>
    private Control BuildHotkeyWidget(int hkIndex)
    {
        var p = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
        var act = Actions.All[hkIndex];
        var saved = UserSettings.GetKeymap();
        var letter = saved.TryGetValue(act.Id, out var k) && k is not null ? k : act.DefaultKey;

        var prefix = new Label
        {
            Text = "Ctrl+Shift+",
            AutoSize = true,
            ForeColor = ForgeTheme.TextMuted,
            Font = ForgeTheme.Small(),
            Margin = new Padding(0, 6, 4, 0),
        };
        var letterBox = new TextBox
        {
            Text = letter ?? "",
            Width = 32,
            MaxLength = 1,
            TextAlign = HorizontalAlignment.Center,
            Font = ForgeTheme.BodyBold(),
        };
        ForgeTheme.StyleInput(letterBox);
        var status = new Label
        {
            Text = "—",
            AutoSize = true,
            ForeColor = ForgeTheme.TextMuted,
            Font = ForgeTheme.Body(),
            Margin = new Padding(6, 6, 0, 0),
        };

        letterBox.Leave += (_, _) => OnHotkeyLetterChanged(hkIndex + 1, letterBox, status, act);
        _hkLetters[hkIndex] = letterBox;
        _hkStatusLbls[hkIndex] = status;

        p.Controls.Add(prefix);
        p.Controls.Add(letterBox);
        p.Controls.Add(status);
        return p;
    }

    private void OnHotkeyLetterChanged(int hkId, TextBox letterBox, Label status, ActionDef act)
    {
        var letter = letterBox.Text.Trim();
        if (letter.Length == 1 && (char.IsLetter(letter[0]) || char.IsDigit(letter[0])))
        {
            letterBox.Text = letter.ToUpperInvariant();
            var ok = _hotkeys?.Replace(hkId, VirtualKey.Letter(letter[0]), $"Ctrl+Shift+{letter.ToUpperInvariant()}") ?? false;
            UserSettings.SetKeymapEntry(act.Id, letter);
            status.Text = ok ? "✓" : "✗";
            status.ForeColor = ok ? ForgeTheme.Success : ForgeTheme.Error;
            Log($"  [hotkey] {act.Label} → Ctrl+Shift+{letter.ToUpperInvariant()} {(ok ? "OK" : "충돌")}");
        }
        else
        {
            letterBox.Text = "";
            _hotkeys?.Replace(hkId, null, $"{act.Label} (비활성)");
            UserSettings.SetKeymapEntry(act.Id, null);
            status.Text = "—";
            status.ForeColor = ForgeTheme.TextMuted;
            Log($"  [hotkey] {act.Label} 비활성화");
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // GlobalHotkeyManager 시작 — MainForm.OnShown 시점
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
        for (int i = 0; i < results.Count; i++)
        {
            var (label, ok) = results[i];
            var status = _hkStatusLbls[i];
            if (status is not null)
            {
                status.Text = ok ? "✓" : (string.IsNullOrEmpty(_hkLetters[i].Text) ? "—" : "✗");
                status.ForeColor = ok ? ForgeTheme.Success : (string.IsNullOrEmpty(_hkLetters[i].Text) ? ForgeTheme.TextMuted : ForgeTheme.Error);
            }
        }
    }

    public void StopHotkeys() => _hotkeys?.Dispose();

    // ──────────────────────────────────────────────────────────────────
    // 핸들러 (Actions 호출)
    // ──────────────────────────────────────────────────────────────────

    private void SafeInvoke(ActionDef act)
    {
        if (Main is null) return;
        if (!Main.EnsureHwp(allowSpawn: false))
        {
            Log($"[{act.Label}] 한/글 attach 실패 — 한/글 먼저 실행해 주세요.");
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
        // Python _run_combined_async / _combined_one_paragraph 1:1 (realtime_tab.py:1216-).
        // apply_per_paragraph 한 번 호출 + 콜백 안에서 매 문단 (들·자·들) 3단계 연속.
        //
        // ★ 직전 버전 (들·자·들 각각 ApplyPerParagraph 별도 호출) 의 두 버그:
        //   1) 첫 ApplyPerParagraph 가 selection 을 Cancel — 2/3차 호출이 단일캐럿
        //      모드로 빠져 현재 문단 1개만 처리. 다중 문단 선택 시 들·자·들이
        //      전체에 일관 적용되지 못함.
        //   2) Python 은 한 문단 안에서 들→자→들 연속이어야 자간조정의 wrap 위치
        //      계산이 인덴트와 일치. 들(전체)→자(전체)→들(전체) 순서는 의미 깨짐.
        //
        // ★ 매 단계 사이 startPos 복원:
        //   ProcessParagraph/AdjustParagraph 끝나면 caret 은 다음 문단 시작 부근으로
        //   이동 — 다음 단계가 같은 문단을 처리하려면 시작 위치로 되돌려야 함.
        //   SetCaretPos 실패 시 MoveParaBegin fallback (Python 동일).

        if (_state.Hwp is null) return;
        Forge.Core.Linter.LogFn lg = msg => Log("  " + msg);

        Forge.Core.Linter.ParaActionFn combined = (hwpObj, logArg) =>
        {
            dynamic h = hwpObj;
            var log = logArg ?? (msg => { });

            h.Run("MoveParaBegin");
            var startPos = Forge.Core.Linter.Range.GetCaretPos(h);
            log($"[combined] 문단 시작 pos={startPos}");

            void Restore(string stage)
            {
                try
                {
                    Forge.Core.Linter.Range.SetCaretPos(h, startPos);
                    log($"[restore→{stage}] SetCaretPos → {startPos}");
                }
                catch (Exception e)
                {
                    log($"[restore→{stage}] 복원 실패 ({e.Message}) — MoveParaBegin fallback");
                    h.Run("MoveParaBegin");
                }
            }

            // 1) 1차 들여쓰기 — line wrap 기준 확정 (없으면 자간 효과 죽음)
            log("--- 1단계: 들여쓰기 정렬 (wrap 기준 확정) ---");
            Forge.Core.Linter.IndentAlign.ProcessParagraph(h, log);

            // 2) 자간조정 — 확정된 wrap 위에서 어절 잘림 보정
            Restore("자간");
            log("--- 2단계: 자간조정 ---");
            Forge.Core.Linter.Kerning.AdjustParagraph(h, log);

            // 3) 2차 들여쓰기 — 자간 drift 보정
            Restore("재정렬");
            log("--- 3단계: 들여쓰기 재정렬 (drift 보정) ---");
            Forge.Core.Linter.IndentAlign.ProcessParagraph(h, log);
        };

        Log("━━━━━━ 자동 정렬 (들·자·들) 시작 ━━━━━━");
        Forge.Core.Linter.Range.ApplyPerParagraph(_state.Hwp.Hwp, combined, lg);
        Log("━━━━━━ 자동 정렬 완료 ━━━━━━");
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
        Primitives.ResetKerningZero(_state.Hwp.Hwp);
    }

    public void RunParagraphSize8()
    {
        if (_state.Hwp is null) return;
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

    public void Log(string msg)
    {
        if (_logOutput is null || _logOutput.IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => Log(msg))); return; }
        _logOutput.AppendText(msg + Environment.NewLine);
        _logOutput.SelectionStart = _logOutput.TextLength;
        _logOutput.ScrollToCaret();
    }
}
