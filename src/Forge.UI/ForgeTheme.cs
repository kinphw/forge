// Forge UI 공용 디자인 토큰 — 색상 팔레트 / 폰트 / 패딩 / 여백.
// 일관된 시각 통일을 위해 모든 폼/탭/컨트롤이 이 클래스의 상수를 사용.

namespace Forge.UI;

public static class ForgeTheme
{
    // ─ 색상 팔레트 (ReportSpec 색상에서 일부 차용) ───────────────────────
    public static readonly Color Background = Color.FromArgb(248, 249, 251);
    public static readonly Color PanelBg    = Color.FromArgb(255, 255, 255);
    public static readonly Color GroupBg    = Color.FromArgb(252, 252, 253);
    public static readonly Color Border     = Color.FromArgb(220, 224, 230);
    public static readonly Color Subtle     = Color.FromArgb(108, 117, 125);
    public static readonly Color TextPrimary= Color.FromArgb(33, 37, 41);
    public static readonly Color TextMuted  = Color.FromArgb(120, 124, 130);

    // 액센트 — Section 박스의 파란 underline 과 일관
    public static readonly Color Accent     = Color.FromArgb(62, 87, 165);
    public static readonly Color AccentLight= Color.FromArgb(224, 229, 250);

    // 상태 색
    public static readonly Color Success    = Color.FromArgb(34, 139, 87);
    public static readonly Color Warning    = Color.FromArgb(217, 119, 6);
    public static readonly Color Error      = Color.FromArgb(220, 38, 38);

    // 로그 패널
    public static readonly Color LogBg      = Color.FromArgb(245, 246, 248);
    public static readonly Color LogText    = Color.FromArgb(60, 65, 75);

    // ─ 폰트 ─────────────────────────────────────────────────────────────
    // Segoe UI = Windows default, 맑은 고딕 = 한글 fallback.
    private const string FamilyPrimary = "Segoe UI";
    private const string FamilyMono = "Consolas";

    public static Font H1()      => new(FamilyPrimary, 16f, FontStyle.Bold);
    public static Font H2()      => new(FamilyPrimary, 12f, FontStyle.Bold);
    public static Font Body()    => new(FamilyPrimary, 10f);
    public static Font BodyBold()=> new(FamilyPrimary, 10f, FontStyle.Bold);
    public static Font Small()   => new(FamilyPrimary, 9f);
    public static Font Mono()    => new(FamilyMono, 9f);
    public static Font Hint()    => new(FamilyPrimary, 9f, FontStyle.Italic);

    // ─ 패딩/여백 ───────────────────────────────────────────────────────
    public const int Pad     = 8;
    public const int PadLg   = 16;
    public const int PadXs   = 4;
    public const int RowGap  = 6;

    public static Padding PanelPadding => new(PadLg);
    public static Padding GroupPadding => new(Pad, Pad + 4, Pad, Pad);

    // ─ 액세서리 ────────────────────────────────────────────────────────

    /// <summary>flat 스타일 버튼 (default Win10 보다 깔끔).</summary>
    public static void StyleFlatButton(Button b, bool accent = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = accent ? Accent : Border;
        b.FlatAppearance.MouseOverBackColor = accent ? AccentLight : Color.FromArgb(240, 242, 245);
        b.BackColor = accent ? Accent : PanelBg;
        b.ForeColor = accent ? Color.White : TextPrimary;
        b.Font = BodyBold();
        b.Padding = new Padding(8, 4, 8, 4);
        b.Cursor = Cursors.Hand;
        b.AutoSize = true;
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.MinimumSize = new Size(0, 30);
    }

    /// <summary>입력 칸 (TextBox / ComboBox 공통).</summary>
    public static void StyleInput(Control c)
    {
        c.Font = Body();
        if (c is TextBox tb)
        {
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = PanelBg;
        }
    }

    /// <summary>GroupBox — 헤더 진하고, 박스는 옅게.</summary>
    public static void StyleGroup(GroupBox g)
    {
        g.Font = BodyBold();
        g.ForeColor = Accent;
        g.BackColor = GroupBg;
        g.Padding = GroupPadding;
    }

    /// <summary>섹션 헤더 라벨 — 탭 안 큰 제목.</summary>
    public static Label SectionTitle(string text) => new()
    {
        Text = text,
        Font = H1(),
        ForeColor = Accent,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 4),
    };

    /// <summary>섹션 설명 — 작은 회색 텍스트.</summary>
    public static Label SectionDesc(string text) => new()
    {
        Text = text,
        Font = Small(),
        ForeColor = TextMuted,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, PadLg),
    };

    /// <summary>가로 구분선.</summary>
    public static Control HorizontalRule(int margin = 6) => new Panel
    {
        Height = 1,
        Dock = DockStyle.Top,
        BackColor = Border,
        Margin = new Padding(0, margin, 0, margin),
    };
}
