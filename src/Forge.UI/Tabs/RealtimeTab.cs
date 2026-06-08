// 탭 ① 실시간 작업.
//
// 구조:
//   상단 meta bar — 로그 비우기 + 설정 초기화
//   3-컬럼 grid [버튼 | 옵션/폰트cluster | 단축키]:
//     그룹 1 (정렬): 자동 정렬 (Q) [center: 마커 사이 빈줄 자동삽입 체크박스] / 어절 끌어올림 (W)
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

    /// <summary>Q 자동정렬 pre-pass — 마커 문단 연속 시 사이에 빈줄(BlankSize) 자동 삽입.</summary>
    public bool BlankBetweenMarkers { get; private set; } = false;

    private CheckBox _qBlankBetween = null!;
    private readonly TextBox[] _hkLetters = new TextBox[Actions.All.Count];
    private readonly Label[] _hkStatusLbls = new Label[Actions.All.Count];

    // 설정 초기화 시 즉시 UI 갱신용 ref. 4 폰트 cluster + blank size + (qBlankBetween 은 별도 필드).
    private readonly ComboBox[] _fontCombos = new ComboBox[4];
    private readonly TextBox[]  _fontSizes  = new TextBox[4];
    private TextBox _blankSizeBox = null!;

    private TextBox _logOutput = null!;
    private GlobalHotkeyManager? _hotkeys;
    private System.Windows.Forms.Timer? _persistTimer;
    private readonly Dictionary<string, object?> _pendingPersist = new();

    // ─ 호버 미리보기 (전/후 PNG 토글) ─
    private PictureBox _previewPicture = null!;
    private Label _previewLabel = null!;
    private Label _previewBadge = null!;   // 현재 프레임 표시 — "이전" / "이후"
    private System.Windows.Forms.Timer? _previewToggleTimer;
    private const int PreviewToggleMs = 900;
    // action_id → (before, after). 둘 다 null 이면 "PNG 없음" (호버 시 무동작).
    private readonly Dictionary<string, (Image? Before, Image? After)> _previewCache = new();
    private Image? _curBefore;
    private Image? _curAfter;
    private bool _showingAfter;   // 현재 PictureBox 가 after 프레임이면 true

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
        BlankBetweenMarkers = GetBool(rt, "q_blank_between_markers", BlankBetweenMarkers);
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

    private static bool GetBool(Dictionary<string, System.Text.Json.JsonElement> d, string key, bool fallback)
    {
        if (!d.TryGetValue(key, out var v)) return fallback;
        if (v.ValueKind == System.Text.Json.JsonValueKind.True) return true;
        if (v.ValueKind == System.Text.Json.JsonValueKind.False) return false;
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

        // ─ 우측 = 미리보기 (상 60%) + 로그 (하 40%) ─
        var rightSplit = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        rightSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        rightSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

        rightSplit.Controls.Add(BuildPreviewPanel(), 0, 0);

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
            Margin = new Padding(0, ForgeTheme.Pad, 0, 0),
        };
        rightSplit.Controls.Add(_logOutput, 0, 1);

        root.Controls.Add(rightSplit, 1, 0);
    }

    private Control BuildPreviewPanel()
    {
        var box = new GroupBox
        {
            Text = "미리보기 (버튼 호버 — 전/후 토글)",
            Dock = DockStyle.Fill,
        };
        ForgeTheme.StyleGroup(box);

        // 상단 배지 — 현재 프레임을 "이전" / "이후" 큰 글씨 + 색상으로 명시.
        _previewBadge = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ForgeTheme.BodyBold(),
            ForeColor = Color.White,
            BackColor = ForgeTheme.TextMuted,
            Text = "",
            Visible = false,
        };
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
            Text = "버튼에 마우스를 올리면 전/후 변화가 토글됩니다.",
            Font = ForgeTheme.Body(),
            ForeColor = ForgeTheme.TextMuted,
            BackColor = Color.White,
        };
        // 추가 순서: Fill (picture) → Top (badge) → Fill overlay (placeholder).
        //   Dock=Top 은 추가 시점의 남은 공간 상단을 차지하므로 picture 가 먼저여야 함.
        box.Controls.Add(_previewPicture);
        box.Controls.Add(_previewBadge);
        box.Controls.Add(_previewLabel);
        _previewLabel.BringToFront();

        _previewToggleTimer = new System.Windows.Forms.Timer { Interval = PreviewToggleMs };
        _previewToggleTimer.Tick += (_, _) => TogglePreviewFrame();
        return box;
    }

    /// <summary>배지 텍스트/색상 갱신. showingAfter=true → "이후 (after)" 강조색.</summary>
    private void UpdateBadge()
    {
        if (_previewBadge is null) return;
        if (_showingAfter)
        {
            _previewBadge.Text = "▶  이 후  (after)";
            _previewBadge.BackColor = ForgeTheme.Success;   // 변환 후 — 강조
        }
        else
        {
            _previewBadge.Text = "◀  이 전  (before)";
            _previewBadge.BackColor = ForgeTheme.TextMuted;  // 변환 전 — 차분
        }
    }

    /// <summary>
    /// action_id → 우측 패널의 시각 행 번호 (1-indexed, 위에서부터).
    /// PNG 파일명은 {slot:D2}_b.png / _a.png 로 매칭.
    /// 새 액션 추가 시 여기 한 줄 추가하면 됨.
    /// </summary>
    private static readonly Dictionary<string, int> PreviewSlots = new()
    {
        ["auto_align"]      = 1,
        ["word_pull"]       = 2,
        ["font_body"]       = 3,
        ["font_annotation"] = 4,
        ["para_size_8"]     = 5,
        ["font_headline"]   = 6,
        ["font_uleungdo"]   = 7,
        ["kerning_reset"]   = 8,
        ["md_convert_sel"]  = 9,
        ["margin_capture"]  = 10,
        ["margin_apply"]    = 11,
    };

    /// <summary>
    /// 임베드 매니페스트 리소스 Forge.RealtimePreviews.{slot:D2}_b.png + _a.png 로드 (cache).
    /// 둘 중 하나만 있어도 OK (toggle 시 그것만 보임). 둘 다 없으면 (null, null).
    /// </summary>
    private (Image? Before, Image? After) LoadPreviewPair(string actionId)
    {
        if (_previewCache.TryGetValue(actionId, out var cached)) return cached;
        if (!PreviewSlots.TryGetValue(actionId, out var slot))
        {
            _previewCache[actionId] = (null, null);
            return (null, null);
        }

        Image? load(string suffix)
        {
            try
            {
                var name = $"Forge.RealtimePreviews.{slot:D2}_{suffix}.png";
                using var manifest = System.Reflection.Assembly
                    .GetExecutingAssembly().GetManifestResourceStream(name);
                if (manifest is not null)
                {
                    // Image.FromStream stream lifetime — MemoryStream 복사 (Image 가 ref 유지).
                    var ms = new MemoryStream();
                    manifest.CopyTo(ms);
                    ms.Position = 0;
                    return Image.FromStream(ms);
                }
            }
            catch { /* 손상 PNG — null */ }
            return null;
        }
        var pair = (load("b"), load("a"));
        _previewCache[actionId] = pair;
        return pair;
    }

    /// <summary>호버 시 호출 — 새 액션의 before/after 로드 후 토글 시작.</summary>
    private void StartPreviewFor(string actionId)
    {
        var (before, after) = LoadPreviewPair(actionId);
        if (before is null && after is null) return;   // PNG 없음 — 직전 표시 유지

        _curBefore = before;
        _curAfter = after;
        // 첫 프레임 = before (없으면 after). after 한 장만 있을 땐 showingAfter 로 마킹.
        if (before is not null)
        {
            _showingAfter = false;
            _previewPicture.Image = before;
        }
        else
        {
            _showingAfter = true;
            _previewPicture.Image = after;
        }
        _previewLabel.Visible = false;
        _previewBadge.Visible = true;
        UpdateBadge();

        if (before is not null && after is not null)
            _previewToggleTimer?.Start();
        else
            _previewToggleTimer?.Stop();   // 한 장만 있으면 토글 의미 없음
    }

    private void TogglePreviewFrame()
    {
        if (_curBefore is null || _curAfter is null) return;
        _showingAfter = !_showingAfter;
        _previewPicture.Image = _showingAfter ? _curAfter : _curBefore;
        UpdateBadge();
    }

    private Panel BuildMetaBar()
    {
        var bar = new Panel
        {
            AutoSize = false,
            Height = 44,           // 버튼(32px) + Location.Y(4) + 하단 여유 8 — GroupBox 와 겹침 방지
            Margin = new Padding(0, 0, 0, ForgeTheme.Pad),
        };
        var clearBtn = new Button { Text = "로그 비우기" };
        ForgeTheme.StyleFlatButton(clearBtn, glyph: MdlIcon.Clear);
        clearBtn.Click += (_, _) => _logOutput.Clear();
        clearBtn.Location = new Point(0, 4);
        bar.Controls.Add(clearBtn);
        _tooltip.SetToolTip(clearBtn, "로그 영역의 모든 내용 지우기");

        var resetBtn = new Button { Text = "설정 초기화" };
        ForgeTheme.StyleFlatButton(resetBtn, glyph: MdlIcon.Refresh);
        resetBtn.Click += (_, _) => OnResetSettings();
        resetBtn.Location = new Point(140, 4);
        bar.Controls.Add(resetBtn);
        _tooltip.SetToolTip(resetBtn,
            "폰트·크기·단축키를 모두 default 로 복원.\n" +
            "한/글 적용 동작은 즉시 default. UI 입력 칸은 다음 앱 시작 시 반영.");

        bar.Width = 320;
        return bar;
    }

    /// <summary>
    /// "마커 사이 빈줄 자동삽입" 체크박스 — 자동정렬(Q) 행의 center 컬럼에 들어감.
    /// 단축키는 가변(letter 변경 가능)이라 라벨에서 'Q' 표기는 제거.
    /// </summary>
    private Control BuildBlankBetweenCheckbox()
    {
        _qBlankBetween = new CheckBox
        {
            Text = "마커 사이 빈줄 자동삽입",
            AutoSize = true,
            Font = ForgeTheme.Body(),
            Checked = BlankBetweenMarkers,
            Margin = new Padding(0, 6, 0, 0),
        };
        _qBlankBetween.CheckedChanged += (_, _) =>
        {
            BlankBetweenMarkers = _qBlankBetween.Checked;
            QueuePersist("q_blank_between_markers", BlankBetweenMarkers);
        };
        _tooltip.SetToolTip(_qBlankBetween,
            "자동정렬 시, 여러 줄 선택 영역에서 마커 문단(□ ○ * ※ Ⅰ. 가. 등) 이\n" +
            "개행 분리 없이 연속되면 사이에 빈줄 자동 삽입 + 빈줄 글자크기는 d 값.");
        return _qBlankBetween;
    }

    /// <summary>
    /// 폰트(4) + 빈줄 크기 + 단축키 letter + 토글을 모두 default 로 복원.
    /// 메모리 상태 + UserSettings 의 realtime·keymap 섹션 + UI 입력 칸까지 즉시 reset.
    /// </summary>
    private void OnResetSettings()
    {
        var dr = MessageBox.Show(FindForm(),
            "RealtimeTab 의 폰트·크기·단축키·토글을 모두 default 로 초기화합니다.\n\n" +
            "• 메모리 상태 + UI 입력 칸 (폰트 콤보·크기 박스·단축키 letter) 즉시 복원.\n" +
            "• 한/글 단축키 재등록 + UserSettings 의 realtime/keymap 섹션 삭제.\n\n" +
            "계속하시겠습니까?",
            "설정 초기화",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (dr != DialogResult.OK) return;

        Font1Name = "휴먼명조";    Font1Size = 15.0;
        Font2Name = "맑은 고딕";    Font2Size = 12.0;
        Font3Name = "HY헤드라인M"; Font3Size = 15.0;
        Font4Name = "HY울릉도M";   Font4Size = 15.0;
        BlankSize = 8.0;
        BlankBetweenMarkers = false;

        // pending debounce 폐기 — reset 직전의 미반영 변경이 살아남는 사고 방지
        _pendingPersist.Clear();
        _persistTimer?.Stop();

        var okRt = UserSettings.RemoveSection("realtime");
        var okKm = UserSettings.RemoveSection("keymap");

        // ─ UI 즉시 갱신 ─
        // 폰트 cluster (combo + size). TextChanged 가 다시 QueuePersist 를 부르지만 default 값이
        // 다시 저장되는 무해한 사이클 — 결과는 동일.
        var fontNames = new[] { Font1Name, Font2Name, Font3Name, Font4Name };
        var fontSizes = new[] { Font1Size, Font2Size, Font3Size, Font4Size };
        for (int i = 0; i < 4; i++)
        {
            if (_fontCombos[i] is not null) _fontCombos[i].Text = fontNames[i];
            if (_fontSizes[i]  is not null) _fontSizes[i].Text  = fontSizes[i].ToString("0.#");
        }
        if (_blankSizeBox is not null) _blankSizeBox.Text = BlankSize.ToString("0.#");
        if (_qBlankBetween is not null) _qBlankBetween.Checked = BlankBetweenMarkers;

        // 단축키 letter + status 라벨 + Win32 재등록
        for (int i = 0; i < Actions.All.Count; i++)
        {
            var act = Actions.All[i];
            if (_hkLetters[i] is not null) _hkLetters[i].Text = act.DefaultKey;
            string letter = act.DefaultKey;
            uint? vk = letter.Length == 1 ? VirtualKey.Letter(letter[0]) : null;
            int hkId = i + 1;
            bool ok = _hotkeys?.Replace(hkId, vk,
                vk is null ? $"{act.Label} (비활성)" : $"Ctrl+Shift+{letter}") ?? false;
            if (_hkStatusLbls[i] is not null)
            {
                _hkStatusLbls[i].Text = vk is null ? "—" : (ok ? "✓" : "✗");
                _hkStatusLbls[i].ForeColor = vk is null
                    ? ForgeTheme.TextMuted
                    : (ok ? ForgeTheme.Success : ForgeTheme.Error);
            }
        }

        Log($"✔ 설정 초기화 완료 (realtime={(okRt ? "OK" : "FAIL")}, keymap={(okKm ? "OK" : "FAIL")}).");
        Log("  메모리·UI·단축키 즉시 default 로 복원됨.");
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
            // 14 button 행 (~36px each, AutoSize RowStyle 보장) + separator × 3 (~14px) +
            // GroupBox header/padding (~30) — 부족 시 행 잘림 사고.
            // (AutoSize=true 는 grid Dock=Fill 과 circular sizing → 시도 금지.)
            Height = 640,
            MinimumSize = new Size(640, 0),
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

        // 행 0: 자동 정렬 (Q) — center 컬럼에 "마커 사이 빈줄 자동삽입" 토글 동거
        AddRow(grid, 0, "자동 정렬 (들·자·들)", BuildBlankBetweenCheckbox(), null,
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
            BuildFontCluster(0, Font1Name, Font1Size, (n, s) => { Font1Name = n; Font1Size = s; QueuePersist("font1", n); QueuePersist("size1", s); }),
            null, hkIndex: 2,
            tooltip: "선택영역 폰트·크기 (본문) — 우측 입력값 적용");

        // 행 4: 주석 폰트 (S)
        AddRow(grid, 4, "폰트·크기 (주석)",
            BuildFontCluster(1, Font2Name, Font2Size, (n, s) => { Font2Name = n; Font2Size = s; QueuePersist("font2", n); QueuePersist("size2", s); }),
            null, hkIndex: 3,
            tooltip: "선택영역 폰트·크기 (주석) — 우측 입력값 적용");

        // 행 5: 빈줄용 크기 (D, A/S/D/F/G 순)
        AddRow(grid, 5, "현재 문단 → 글자크기",
            BuildSizeOnlyCluster(BlankSize, s => { BlankSize = s; QueuePersist("blank_size", s); }),
            null, hkIndex: 6,
            tooltip: "빈줄 용 글자크기 설정 (자간 꼬임 회피)");

        // 행 6: 헤드라인 폰트 (F)
        AddRow(grid, 6, "폰트·크기 (헤드라인)",
            BuildFontCluster(2, Font3Name, Font3Size, (n, s) => { Font3Name = n; Font3Size = s; QueuePersist("font3", n); QueuePersist("size3", s); }),
            null, hkIndex: 4,
            tooltip: "선택영역 폰트·크기 (헤드라인) — 우측 입력값 적용");

        // 행 7: 울릉도 폰트 (G)
        AddRow(grid, 7, "폰트·크기 (울릉도)",
            BuildFontCluster(3, Font4Name, Font4Size, (n, s) => { Font4Name = n; Font4Size = s; QueuePersist("font4", n); QueuePersist("size4", s); }),
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

        // 행 11: separator
        AddSeparator(grid, 11);

        // 행 12: 여백 캡쳐 (기본 단축키 빈칸)
        AddRow(grid, 12, "여백 캡쳐 (현재 문서 → 클립보드)", null, null, hkIndex: 9,
            tooltip: "현재 한/글 문서의 6변 여백 (Left/Right/Top/Bottom/Header/Footer) 을\n" +
                     "세션 클립보드에 저장 (앱 종료 시 휘발).");

        // 행 13: 여백 적용 (기본 단축키 빈칸)
        AddRow(grid, 13, "여백 적용 (클립보드 → 현재 문서)", null, null, hkIndex: 10,
            tooltip: "클립보드에 저장된 여백을 현재 한/글 문서 전체에 적용.");

        box.Controls.Add(grid);
        return box;
    }

    private void AddRow(TableLayoutPanel grid, int row, string buttonText, Control? center, Control? right, int hkIndex, string tooltip)
    {
        EnsureRowStyle(grid, row);
        var act = Actions.All[hkIndex];
        var btn = new Button { Text = buttonText, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(210, 32), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 8, 0) };
        ForgeTheme.StyleFlatButton(btn);
        btn.Click += (_, _) => SafeInvoke(act);
        _tooltip.SetToolTip(btn, tooltip);
        // 호버 시 우측 미리보기 패널에 전/후 PNG 토글 시작.
        btn.MouseEnter += (_, _) => StartPreviewFor(act.Id);
        grid.Controls.Add(btn, 0, row);

        if (center is not null)
            grid.Controls.Add(center, 1, row);
        else
            grid.Controls.Add(new Label { AutoSize = true, Text = "" }, 1, row);

        grid.Controls.Add(BuildHotkeyWidget(hkIndex), 2, row);
    }

    private void AddSeparator(TableLayoutPanel grid, int row)
    {
        EnsureRowStyle(grid, row);
        var sep = new Panel { Height = 1, BackColor = ForgeTheme.Border, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6) };
        grid.Controls.Add(sep, 0, row);
        grid.SetColumnSpan(sep, 3);
    }

    /// <summary>
    /// TableLayoutPanel 의 해당 row 에 AutoSize RowStyle 보장.
    /// 기본 RowStyle 은 Absolute(20) 이라 32px MinimumSize 버튼이 다음 행을 침범 →
    /// 마지막 행이 GroupBox 하단을 벗어나서 안 보이는 사고 회피.
    /// </summary>
    private static void EnsureRowStyle(TableLayoutPanel grid, int row)
    {
        while (grid.RowStyles.Count <= row)
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    }

    /// <summary>폰트 cluster — [ComboBox: 폰트명] [TextBox: pt]. slot: 0..3 → _fontCombos/_fontSizes 저장.</summary>
    private Control BuildFontCluster(int slot, string initName, double initSize, Action<string, double> onChange)
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

        _fontCombos[slot] = combo;
        _fontSizes[slot] = size;

        p.Controls.Add(combo, 0, 0);
        p.Controls.Add(size, 1, 0);
        return p;
    }

    /// <summary>크기-only cluster — [TextBox: pt] (행 5 빈줄용). _blankSizeBox 에 ref 저장.</summary>
    private Control BuildSizeOnlyCluster(double initSize, Action<double> onChange)
    {
        var p = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
        var size = new TextBox { Text = initSize.ToString("0.#"), Width = 60, Font = ForgeTheme.Body() };
        ForgeTheme.StyleInput(size);
        size.TextChanged += (_, _) => { if (double.TryParse(size.Text, out var s)) onChange(s); };
        _blankSizeBox = size;
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

        // 들여쓰기·자간만 조정하고 글자 수는 불변 (IndentAlign 은 ParagraphShapeIndentAtCaret,
        // Kerning 은 CharShape — 둘 다 텍스트 삽입 없음). 캐럿만 있는 경우 (selection 없음,
        // 따라서 빈줄 삽입 pre-pass 도 안 탐) 작업 후 원래 캐럿 위치로 복원한다.
        object hwpObjForOrigin = (object)_state.Hwp.Hwp;
        var selForOrigin = Forge.Core.Linter.Range.SelectionRange(hwpObjForOrigin);
        Forge.Core.Linter.CaretPos? origin =
            selForOrigin is null ? Forge.Core.Linter.Range.GetCaretPos(_state.Hwp.Hwp) : null;

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

        // ─ Pre-pass: 마커 연속 문단 사이에 빈줄 자동 삽입 (체크박스 토글) ─
        //
        // ★ pre-pass 가 selection 을 Cancel 하므로, pre-pass 가 실행된 경우엔 insertion
        //   개수와 무관하게 saved 범위로 ApplyPerParagraphInRange 를 호출해야 함.
        //   (이전 회귀: 0건 삽입 시 fall-through 로 ApplyPerParagraph → SelectionRange null
        //   → 단일 캐럿 모드로 빠져서 1 문단만 처리.)
        Forge.Core.Linter.CaretPos? rangeStart = null;
        Forge.Core.Linter.CaretPos? rangeEnd = null;
        if (BlankBetweenMarkers)
        {
            object hwpObj = (object)_state.Hwp.Hwp;
            var selBefore = Forge.Core.Linter.Range.SelectionRange(hwpObj);
            if (selBefore.HasValue)
            {
                Log("[STAGE 0] 마커 연속 → 빈줄 자동 삽입 pre-pass");
                int inserted = Forge.Core.Linter.IndentAlign.InsertBlanksBetweenMarkers(
                    _state.Hwp.Hwp, BlankSize, lg);
                rangeStart = selBefore.Value.Start;
                rangeEnd = selBefore.Value.End with { Para = selBefore.Value.End.Para + inserted };
                if (inserted > 0)
                    Log($"  ★ 정렬 영역 확장: end.Para {selBefore.Value.End.Para} → {rangeEnd.Value.Para} (+{inserted})");
                else
                    Log($"  · 삽입 없음 — 원래 범위 그대로 ({rangeStart.Value} → {rangeEnd.Value})");
            }
        }

        Log("━━━━━━ 자동 정렬 (들·자·들) 시작 ━━━━━━");
        if (rangeStart.HasValue && rangeEnd.HasValue)
        {
            Forge.Core.Linter.Range.ApplyPerParagraphInRange(
                _state.Hwp.Hwp, combined, rangeStart.Value, rangeEnd.Value, lg);
        }
        else
        {
            Forge.Core.Linter.Range.ApplyPerParagraph(_state.Hwp.Hwp, combined, lg);
        }

        if (origin is not null)
        {
            try { Forge.Core.Linter.Range.SetCaretPos(_state.Hwp.Hwp, origin.Value); }
            catch { ((dynamic)_state.Hwp.Hwp).HAction.Run("MoveParaBegin"); }
        }
        Log("━━━━━━ 자동 정렬 완료 ━━━━━━");
    }

    public void RunWordPull()
    {
        if (_state.Hwp is null) return;
        dynamic hwp = _state.Hwp.Hwp;
        Forge.Core.Linter.LogFn lg = msg => Log("  " + msg);

        // 자간만 조정하고 글자 수는 불변 — 캐럿만 있을 땐 원위치 복원 (RunKerningReset 과 동일).
        object hwpObj = hwp;
        var sel = Forge.Core.Linter.Range.SelectionRange(hwpObj);
        Forge.Core.Linter.CaretPos? origin =
            sel is null ? Forge.Core.Linter.Range.GetCaretPos(hwp) : null;

        Forge.Core.Linter.Squeeze.FitCurrentParagraphToOneLine(hwp, log: lg);

        if (origin is not null)
        {
            try { Forge.Core.Linter.Range.SetCaretPos(hwp, origin.Value); }
            catch { hwp.HAction.Run("MoveParaBegin"); }
        }
    }

    public void RunKerningReset()
    {
        if (_state.Hwp is null) return;
        dynamic hwp = _state.Hwp.Hwp;

        // ★ dynamic dispatch 회피 — SelectionRange 는 object 로 정적 호출 (Range.cs 주석 참조).
        object hwpObj = hwp;
        var sel = Forge.Core.Linter.Range.SelectionRange(hwpObj);
        if (sel is null)
        {
            // 캐럿만 있는 상태 — 현재 문단 전체를 선택해 자간 0 적용.
            // 자간만 바꾸고 문단 구조는 불변이므로, 적용 전 캐럿 위치를 저장했다
            // 적용 후 그대로 복원한다 (RunAutoAlign 의 Restore 와 동일 패턴).
            var origin = Forge.Core.Linter.Range.GetCaretPos(hwp);
            hwp.HAction.Run("MoveParaBegin");
            hwp.HAction.Run("MoveSelParaEnd");
            Primitives.ResetKerningZero(hwp);
            hwp.HAction.Run("Cancel");
            try { Forge.Core.Linter.Range.SetCaretPos(hwp, origin); }
            catch { hwp.HAction.Run("MoveParaBegin"); }
        }
        else
        {
            // 선택영역이 있으면 그 범위에만 적용.
            Primitives.ResetKerningZero(hwp);
        }
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

    // ─ 여백 클립보드 (세션 한정, 영속화 없음) ─────────────────────────
    private Primitives.PageMargins? _marginClipboard;

    public void RunMarginCapture()
    {
        if (_state.Hwp is null) return;
        try
        {
            _marginClipboard = Primitives.GetPageMargins(_state.Hwp.Hwp);
            var m = _marginClipboard;
            Log($"[여백 캡쳐] L={m.Left:0.##} R={m.Right:0.##} T={m.Top:0.##} " +
                $"B={m.Bottom:0.##} H={m.Header:0.##} F={m.Footer:0.##} (mm)");
        }
        catch (Exception ex)
        {
            Log($"[여백 캡쳐] ✘ {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void RunMarginApply()
    {
        if (_state.Hwp is null) return;
        if (_marginClipboard is null)
        {
            Log("[여백 적용] 클립보드 비어있음 — 먼저 '여백 캡쳐' 버튼으로 다른 문서에서 캡쳐하세요.");
            return;
        }
        try
        {
            var m = _marginClipboard;
            Primitives.SetPageMargins(_state.Hwp.Hwp, m.Left, m.Right, m.Top, m.Bottom, m.Header, m.Footer);
            Log($"[여백 적용] L={m.Left:0.##} R={m.Right:0.##} T={m.Top:0.##} " +
                $"B={m.Bottom:0.##} H={m.Header:0.##} F={m.Footer:0.##} (mm) — 현재 구역 적용");
        }
        catch (Exception ex)
        {
            Log($"[여백 적용] ✘ {ex.GetType().Name}: {ex.Message}");
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewToggleTimer?.Stop();
            _previewToggleTimer?.Dispose();
            foreach (var (b, a) in _previewCache.Values)
            {
                b?.Dispose();
                a?.Dispose();
            }
            _previewCache.Clear();
        }
        base.Dispose(disposing);
    }
}
