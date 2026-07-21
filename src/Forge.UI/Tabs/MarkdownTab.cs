// 탭 ③ 마크다운 입력 — md 입력 + 변환 + 저장.
// Python 원본 forge/ui/tabs/markdown_tab.py 1:1 + ForgeTheme styling.

using System.Text.Json;
using Forge.Core;
using Forge.Core.Formatter;
using Forge.Core.Templates;

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
    private Button _cancelButton = null!;
    private Button _saveAsButton = null!;
    private CheckBox _applyAutoFormat = null!;
    private volatile bool _cancelRequested;
    private Label _statusLine = null!;

    // 페이지 spec — 양식 spec bar (margins 6 + 줄간격%)
    private NumericUpDown _mLeft = null!;
    private NumericUpDown _mRight = null!;
    private NumericUpDown _mTop = null!;
    private NumericUpDown _mBottom = null!;
    private NumericUpDown _mHeader = null!;
    private NumericUpDown _mFooter = null!;
    private NumericUpDown _lineDefault = null!;

    // markdown section persist (debounce 500ms)
    private System.Windows.Forms.Timer? _persistTimer;
    private readonly Dictionary<string, object?> _pendingPersist = new();

    private const string SampleMd = """
---
보고서명: ◆◆◆ 검사 결과 및 대응방안
---

1. 현황

가. 개요
□ (배경) **은행권 광고 동의 위반** 사례 적발 — 금감원 정기검사 결과
 ○ **A사**·**B사**가 마케팅 이용·광고수신 동의 미보유 고객의 **개인신용정보** 활용
  - A사: 2024.1~6 약 1.2만 건
  - B사: 2024.3~9 약 0.8만 건
   · 동의 유효기간 경과 케이스 다수 포함

나. 검사 진행
□ (현황) 자료 회신 **90% 수령 완료**
 ○ 잔여 자료 추가 요구 발송 (5.7.) — 회신 기한 5.15.

* 검사 표본은 신용정보법 제33조의2 위반 의심 건 위주 추출
※ 본 수치는 잠정치 — 최종 결과는 검사 종료 시점에 확정
† 위반 건수 = 동의 미보유 + 유효기간 경과 합계 기준

[참고]
신용정보법 제33조의2(개인신용정보의 이용) 제2항
신용정보주체로부터 마케팅 이용·광고수신 동의를 받지 아니하고는 광고를 위하여 개인신용정보를 이용할 수 없다.

2. 향후 계획

가. 즉시 조치
□ (제재) **위반사항 확정** 시 과태료 부과 검토
□ (개선) **동의 관리 체계 점검** 및 시스템 차원 개선 권고

나. 중장기
□ (제도) 광고 동의 갱신 주기 가이드라인 마련
 ○ 업계 의견 수렴 (5월 중) → 감독규정 개정 검토 (6~7월)

=> 검사 결과를 토대로 **업계 차원의 시스템적 개선** 유도

[붙임 1]
관련 법령 발췌
신용정보법 제33조의2 — 개인신용정보 이용 동의 의무
신용정보법 시행령 제27조의2 제3항 — 광고 목적 이용 제한

[붙임 2]
검사 대상 회사별 위반 건수 잠정치
A사: 12,345 건 (2024.1~6 누적)
B사:  8,567 건 (2024.3~9 누적)
합계 약 2.0만 건 — 추가 자료 회신 후 확정.
""";

    public MarkdownTab(AppState state, Action updateStatus)
    {
        _state = state;
        _updateStatus = updateStatus;
        BackColor = ForgeTheme.Background;
        Padding = ForgeTheme.PanelPadding;
        BuildUI();
        ApplyVarsToSpec(silent: true);  // 영속화된 margins/줄간격 → spec 동기화
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
            RowCount = 6,
            Padding = new Padding(0, 0, ForgeTheme.Pad, 0),
            BackColor = ForgeTheme.Background,
        };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // 헤더
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // 설명
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // 메타 (작성부서/일자)
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // 양식 spec bar
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // md 입력
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // 버튼 row

        grid.Controls.Add(ForgeTheme.SectionTitle("마크다운 입력 → 새 hwpx 변환"), 0, 0);
        grid.Controls.Add(ForgeTheme.SectionDesc(
            "개조식 markdown 을 한/글에 새 문서로 변환. front-matter 의 보고서명 + 아래 작성부서·일자로 헤더 자동 생성."),
            0, 1);

        grid.Controls.Add(BuildMetaBar(), 0, 2);
        grid.Controls.Add(BuildSpecBar(), 0, 3);

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
            Font = ForgeTheme.MonoLg(),
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
        grid.Controls.Add(mdBox, 0, 4);

        // 버튼 row
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(0, ForgeTheme.Pad, 0, 0),
        };
        var sampleButton = new Button { Text = "샘플 채우기" };
        ForgeTheme.StyleFlatButton(sampleButton, glyph: MdlIcon.Tag);
        sampleButton.Click += OnFillSample;
        sampleButton.Margin = new Padding(0, 0, 6, 0);

        var clearButton = new Button { Text = "비우기" };
        ForgeTheme.StyleFlatButton(clearButton, glyph: MdlIcon.Clear);
        clearButton.Click += (_, _) => _mdInput.Clear();
        clearButton.Margin = new Padding(0, 0, 12, 0);

        var openButton = new Button { Text = "파일 열기" };
        ForgeTheme.StyleFlatButton(openButton, glyph: MdlIcon.OpenFile);
        openButton.Click += OnOpenFile;
        openButton.Margin = new Padding(0, 0, 12, 0);

        _convertButton = new Button { Text = "변환 (md → hwpx)" };
        ForgeTheme.StyleFlatButton(_convertButton, accent: true, glyph: MdlIcon.Sync);
        _convertButton.Click += OnConvert;
        _convertButton.Margin = new Padding(0, 0, 8, 0);

        // 강제 중지 — 변환 중에만 활성. 클릭 시 _cancelRequested 플래그 → dispatcher 즉시 종료.
        _cancelButton = new Button { Text = "강제 중지", Enabled = false };
        ForgeTheme.StyleFlatButton(_cancelButton, glyph: MdlIcon.Clear);
        _cancelButton.Click += (_, _) =>
        {
            _cancelRequested = true;
            Log("[변환] 사용자 강제 중지 요청 — 다음 노드 경계에서 종료");
            _cancelButton.Enabled = false;
        };
        _cancelButton.Margin = new Padding(0, 0, 8, 0);

        _saveAsButton = new Button { Text = "다른 이름으로 저장", Enabled = false };
        ForgeTheme.StyleFlatButton(_saveAsButton, glyph: MdlIcon.Save);
        _saveAsButton.Click += OnSaveAs;

        // 사후 Q 처리(자동정렬 = 들여쓰기·자간) 활성/생략 체크박스. 기본 활성.
        _applyAutoFormat = new CheckBox
        {
            Text = "사후 자동정렬 (Q)",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(12, 6, 0, 0),
            ForeColor = ForgeTheme.TextPrimary,
        };

        btnPanel.Controls.Add(sampleButton);
        btnPanel.Controls.Add(clearButton);
        btnPanel.Controls.Add(openButton);
        btnPanel.Controls.Add(_convertButton);
        btnPanel.Controls.Add(_cancelButton);
        btnPanel.Controls.Add(_saveAsButton);
        btnPanel.Controls.Add(_applyAutoFormat);
        grid.Controls.Add(btnPanel, 0, 5);

        return grid;
    }

    private Control BuildMetaBar()
    {
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
            ColumnCount = 5,
            RowCount = 1,
            AutoSize = true,
            Padding = new Padding(4),
        };
        metaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        metaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        metaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        metaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        metaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 영속화된 dept 값 로드 (작성일은 의도적 제외 — 어제 날짜 박힘 사고 방지)
        var md = UserSettings.GetSection("markdown");
        var initialDept = md.TryGetValue("dept", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

        metaGrid.Controls.Add(MakeFieldLabel("작성부서"), 0, 0);
        _deptInput = new TextBox { Dock = DockStyle.Fill, Font = ForgeTheme.Body(), Text = initialDept };
        ForgeTheme.StyleInput(_deptInput);
        _deptInput.TextChanged += (_, _) => QueuePersist("dept", _deptInput.Text);
        metaGrid.Controls.Add(_deptInput, 1, 0);

        metaGrid.Controls.Add(MakeFieldLabel("작성일"), 2, 0);
        _dateInput = new TextBox { Dock = DockStyle.Fill, Text = DateTime.Now.ToString("yyyy-MM-dd"), Font = ForgeTheme.Body() };
        ForgeTheme.StyleInput(_dateInput);
        metaGrid.Controls.Add(_dateInput, 3, 0);

        metaGrid.Controls.Add(new Label
        {
            Text = "(YYYY-MM-DD, 비우면 변환 시 오늘)",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = ForgeTheme.Hint(),
            ForeColor = ForgeTheme.TextMuted,
            Margin = new Padding(6, 8, 0, 0),
        }, 4, 0);

        metaBox.Controls.Add(metaGrid);
        return metaBox;
    }

    /// <summary>양식 spec — 페이지 여백 6종 + 줄간격% + 기본값/적용 버튼.
    /// Python markdown_tab.py:131-181 1:1.</summary>
    private Control BuildSpecBar()
    {
        var specBox = new GroupBox
        {
            Text = "양식 spec — 페이지 여백·줄간격",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, ForgeTheme.Pad),
        };
        ForgeTheme.StyleGroup(specBox);

        // ★ 2-row 구조: 스피너 줄 (Dock=Top, 첫 추가 → 위) + 버튼 줄 (Dock=Top, 마지막 추가 → 아래).
        //   FlowLayoutPanel 의 AutoSize+WrapContents 가 함께 안 먹는 (AutoSize 가 폭을 늘려
        //   wrap 발동 안 함) WinForms quirk 회피용. 두 flow 로 명시 분리.

        // 버튼 줄 (Dock=Top 으로 specBox 에 마지막 추가 → 시각상 아래)
        var buttonFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(4, 0, 4, 4),
        };

        // 스피너 줄
        var inputFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(4, 4, 4, 0),
        };

        var md = UserSettings.GetSection("markdown");
        var m = _state.Spec.Margins;

        _mLeft   = AddMarginSpinner(inputFlow, "좌",   GetD(md, "margin_left",   m.Left));
        _mRight  = AddMarginSpinner(inputFlow, "우",   GetD(md, "margin_right",  m.Right));
        _mTop    = AddMarginSpinner(inputFlow, "위",   GetD(md, "margin_top",    m.Top));
        _mBottom = AddMarginSpinner(inputFlow, "아래", GetD(md, "margin_bottom", m.Bottom));
        _mHeader = AddMarginSpinner(inputFlow, "머리", GetD(md, "margin_header", m.Header));
        _mFooter = AddMarginSpinner(inputFlow, "꼬리", GetD(md, "margin_footer", m.Footer));
        inputFlow.Controls.Add(new Label
        {
            Text = "mm", AutoSize = true,
            Font = ForgeTheme.Small(), ForeColor = ForgeTheme.TextMuted,
            Margin = new Padding(0, 8, 12, 0),
        });

        // separator
        inputFlow.Controls.Add(new Label
        {
            Text = "│", AutoSize = true,
            ForeColor = ForgeTheme.Border, Margin = new Padding(0, 6, 8, 0),
        });

        inputFlow.Controls.Add(new Label
        {
            Text = "줄간격", AutoSize = true,
            Font = ForgeTheme.Body(), ForeColor = ForgeTheme.TextPrimary,
            Margin = new Padding(0, 8, 4, 0),
        });
        _lineDefault = new NumericUpDown
        {
            Minimum = 100, Maximum = 300, Increment = 5,
            Value = (decimal)GetD(md, "line_default", _state.Spec.LineSpacingDefault),
            Width = 56, Font = ForgeTheme.Body(),
        };
        _lineDefault.ValueChanged += (_, _) => QueuePersist("line_default", (int)_lineDefault.Value);
        inputFlow.Controls.Add(_lineDefault);
        inputFlow.Controls.Add(new Label
        {
            Text = "%", AutoSize = true,
            Font = ForgeTheme.Small(), ForeColor = ForgeTheme.TextMuted,
            Margin = new Padding(0, 8, 12, 0),
        });

        var resetBtn = new Button { Text = "↺ 기본값", Margin = new Padding(0, 4, 6, 0) };
        ForgeTheme.StyleFlatButton(resetBtn);
        resetBtn.Click += (_, _) => ResetSpec();
        buttonFlow.Controls.Add(resetBtn);

        var applyBtn = new Button { Text = "설정 적용", Margin = new Padding(0, 4, 6, 0) };
        ForgeTheme.StyleFlatButton(applyBtn);
        applyBtn.Click += (_, _) =>
        {
            if (ApplyVarsToSpec(silent: false))
                Log("[spec] 양식 spec 적용됨 — 다음 변환부터 반영");
        };
        buttonFlow.Controls.Add(applyBtn);

        // 현재 한/글 문서(현재 구역) 에 즉시 6변 여백 적용 — 새 파일 변환을 거치지 않고 다이렉트.
        var applyHwpBtn = new Button { Text = "현재 구역에 적용", Margin = new Padding(0, 4, 0, 0) };
        ForgeTheme.StyleFlatButton(applyHwpBtn);
        applyHwpBtn.Click += (_, _) => OnApplyMarginsToCurrentHwp();
        buttonFlow.Controls.Add(applyHwpBtn);

        // Dock=Top stack 추가 순서 = bottom-up: 먼저 추가가 아래, 마지막이 위.
        // 시각 순서 [스피너 위 / 버튼 아래] 를 위해 buttonFlow 먼저 추가, inputFlow 나중.
        specBox.Controls.Add(buttonFlow);
        specBox.Controls.Add(inputFlow);
        return specBox;
    }

    /// <summary>
    /// 6 spinner 값을 현재 한/글 문서에 즉시 Primitives.SetPageMargins 로 적용.
    /// MdConvert 의 새 파일 생성 경로를 거치지 않음 — 활성 문서 그대로 변경.
    /// </summary>
    private void OnApplyMarginsToCurrentHwp()
    {
        if (Main is null) return;
        if (!Main.EnsureHwp(allowSpawn: false))
        {
            Log("[여백] 한/글 attach 실패 — 한/글 먼저 실행해 주세요.");
            return;
        }
        if (_state.Hwp is null) return;

        double L = (double)_mLeft.Value;
        double R = (double)_mRight.Value;
        double T = (double)_mTop.Value;
        double B = (double)_mBottom.Value;
        double H = (double)_mHeader.Value;
        double F = (double)_mFooter.Value;
        try
        {
            Forge.Core.Renderers.Primitives.SetPageMargins(
                _state.Hwp.Hwp, L, R, T, B, H, F);
            Log($"[여백] 현재 구역 적용: L={L:0.##} R={R:0.##} T={T:0.##} " +
                $"B={B:0.##} H={H:0.##} F={F:0.##} (mm)");
        }
        catch (Exception ex)
        {
            Log($"[여백] 적용 실패: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private NumericUpDown AddMarginSpinner(FlowLayoutPanel parent, string label, double initial)
    {
        parent.Controls.Add(new Label
        {
            Text = label, AutoSize = true,
            Font = ForgeTheme.Body(), ForeColor = ForgeTheme.TextMuted,
            Margin = new Padding(0, 8, 2, 0),
        });
        var spin = new NumericUpDown
        {
            Minimum = 0, Maximum = 50, Increment = 0.5m, DecimalPlaces = 1,
            Value = (decimal)initial,
            Width = 56, Font = ForgeTheme.Body(),
            Margin = new Padding(0, 4, 6, 0),
        };
        // value-changed binding 시 key 별 영속화
        var key = label switch
        {
            "좌" => "margin_left", "우" => "margin_right", "위" => "margin_top",
            "아래" => "margin_bottom", "머리" => "margin_header", "꼬리" => "margin_footer",
            _ => "margin_other",
        };
        spin.ValueChanged += (_, _) => QueuePersist(key, (double)spin.Value);
        parent.Controls.Add(spin);
        return spin;
    }

    private static double GetD(Dictionary<string, JsonElement> d, string key, double fallback)
    {
        if (!d.TryGetValue(key, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var p)) return p;
        return fallback;
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
            Font = ForgeTheme.MonoMd(),
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

    // ──────────────────────────────────────────────────────────────────
    // spec 적용 / reset
    // ──────────────────────────────────────────────────────────────────

    /// <summary>spec bar 입력값 → state.Spec.Margins + LineSpacingDefault 반영.
    /// silent=false 면 사용자에게 적용 다이얼로그 노출.</summary>
    private bool ApplyVarsToSpec(bool silent)
    {
        try
        {
            _state.Spec = _state.Spec with
            {
                Margins = new PageMargins
                {
                    Left   = (double)_mLeft.Value,
                    Right  = (double)_mRight.Value,
                    Top    = (double)_mTop.Value,
                    Bottom = (double)_mBottom.Value,
                    Header = (double)_mHeader.Value,
                    Footer = (double)_mFooter.Value,
                },
                LineSpacingDefault = (int)_lineDefault.Value,
            };
            return true;
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show(FindForm(), $"입력 오류: {ex.Message}", "오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void ResetSpec()
    {
        _state.Spec = ReportSpec.Report1;
        var m = _state.Spec.Margins;
        _mLeft.Value   = (decimal)m.Left;
        _mRight.Value  = (decimal)m.Right;
        _mTop.Value    = (decimal)m.Top;
        _mBottom.Value = (decimal)m.Bottom;
        _mHeader.Value = (decimal)m.Header;
        _mFooter.Value = (decimal)m.Footer;
        _lineDefault.Value = _state.Spec.LineSpacingDefault;
        Log("[spec] 기본값(Report1)으로 reset");
    }

    // ──────────────────────────────────────────────────────────────────
    // 영속화 (markdown section, debounce 500ms) — var_date 만 제외
    // ──────────────────────────────────────────────────────────────────

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
        UserSettings.UpdateSection("markdown", new Dictionary<string, object?>(_pendingPersist));
        _pendingPersist.Clear();
    }

    /// <summary>외부 강제 flush — MainForm 종료 / 💾 설정 저장 버튼.</summary>
    public void FlushPersist() => OnPersistFlush(null, EventArgs.Empty);

    // ──────────────────────────────────────────────────────────────────
    // 핸들러
    // ──────────────────────────────────────────────────────────────────

    private void OnFillSample(object? sender, EventArgs e)
    {
        // WinForms TextBox 는 CRLF 만 줄바꿈으로 인식 — raw string 의 LF 정규화.
        _mdInput.Text = SampleMd.ReplaceLineEndings("\r\n");
        _deptInput.Text = "테스트팀";
        Log("[샘플] 샘플 markdown 채움 (작성부서='테스트팀')");
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
            _mdInput.Text = File.ReadAllText(dlg.FileName).ReplaceLineEndings("\r\n");
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
        _cancelRequested = false;
        _cancelButton.Enabled = true;
        // ★ Esc 로도 강제 중지 가능 — 변환 시작 시 form KeyPreview + KeyDown 핸들러 임시 부착.
        var form = FindForm();
        KeyEventHandler? escHandler = null;
        bool prevKeyPreview = false;
        if (form is not null)
        {
            prevKeyPreview = form.KeyPreview;
            form.KeyPreview = true;
            escHandler = (_, ke) =>
            {
                if (ke.KeyCode == Keys.Escape && _cancelButton.Enabled)
                {
                    _cancelRequested = true;
                    Log("[변환] Esc — 강제 중지 요청");
                    _cancelButton.Enabled = false;
                    ke.Handled = true;
                }
            };
            form.KeyDown += escHandler;
        }
        Application.DoEvents();   // 버튼 enable 즉시 paint
        try
        {
            var doc = Parser.Parse(src);
            Log($"  파싱: {doc.Nodes.Count} 노드 (메타 보고서명={doc.Metadata.ReportTitle ?? "(없음)"})");

            // SSOT 주입 — RealtimeTab 의 4 폰트 cluster + BlankSize 를
            // _state.Spec 에 반영 (record `with` — 새 인스턴스로 swap).
            // 동시에 spec bar 의 사용자 입력 (margins/line) 도 살아있어야 하므로
            // ApplyVarsToSpec(silent=true) 를 먼저 호출.
            ApplyVarsToSpec(silent: true);
            try
            {
                var rt = Main.GetRealtimeTab();
                if (rt is not null)
                {
                    _state.Spec = rt.ApplyOverridesToSpec(_state.Spec);
                    Log($"  [ssot] 본문={_state.Spec.Bullets[0].Font}/{_state.Spec.Bullets[0].SizePt}pt, " +
                        $"주석={_state.Spec.Annotation.Font}/{_state.Spec.Annotation.SizePt}pt, " +
                        $"헤드라인={_state.Spec.TitleFont}, 요약={_state.Spec.BulletSummaryFont}, " +
                        $"빈줄={_state.Spec.BlankParaPt}pt");
                }
            }
            catch (Exception ex)
            {
                Log($"  [ssot] override skip ({ex.GetType().Name}) — spec 기본값 사용");
            }

            HwpxWriter.LogFn logFn = msg => Log($"  {msg}");
            // cancel callback — UI 스레드에서 DoEvents 로 버튼 클릭 펌프 + 플래그 반환.
            Func<bool> cancelCheck = () =>
            {
                Application.DoEvents();
                return _cancelRequested;
            };
            bool runQ = _applyAutoFormat.Checked;
            Log($"  [config] 사후 자동정렬(Q) = {(runQ ? "ON" : "OFF — STAGE 2 skip")}");
            HwpxWriter.GenerateHwpxViaCom(
                _state.Hwp.Hwp,
                doc,
                outPath: null,
                spec: _state.Spec,
                log: logFn,
                mode: HwpxWriteMode.New,
                department: _deptInput.Text,
                date: string.IsNullOrWhiteSpace(_dateInput.Text) ? null : _dateInput.Text,
                applyIndentAlign: runQ,
                applyKerning: runQ,
                isCancelled: cancelCheck);

            if (_cancelRequested)
            {
                Log("[변환] ⚠ 사용자 강제 중지로 종료 — 부분 결과는 한/글 문서에 남아 있을 수 있음");
                SetStatus("⚠ 강제 중지됨", ForgeTheme.Warning);
            }
            else
            {
                _saveAsButton.Enabled = true;
                Log("[변환] ✔ 완료 — 한/글 화면 확인 후 [다른 이름으로 저장] 으로 hwpx 저장");
                SetStatus("✔ 변환 완료 — 저장 대기", ForgeTheme.Success);
            }
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
            _cancelButton.Enabled = false;
            if (form is not null && escHandler is not null)
            {
                form.KeyDown -= escHandler;
                form.KeyPreview = prevKeyPreview;
            }
        }
    }

    private void OnSaveAs(object? sender, EventArgs e)
    {
        if (_state.Hwp is null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "한/글 (*.hwpx)|*.hwpx",
            Title = "hwpx 저장",
            FileName = $"forge_draft_{DateTime.Now:yyyyMMdd_HHmmss}.hwpx",
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

    public void Log(string msg)
    {
        if (_logOutput is null || _logOutput.IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => Log(msg))); return; }
        _logOutput.AppendText(msg + Environment.NewLine);
        _logOutput.SelectionStart = _logOutput.TextLength;
        _logOutput.ScrollToCaret();
        // ★ UI 응답성 — 변환 중 빈번한 Log 호출마다 message pump 처리해
        //   강제 중지 버튼 클릭 + Esc 키 이벤트 잡힘.
        if (_cancelButton is { Enabled: true }) Application.DoEvents();
    }
}
