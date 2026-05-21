// Forge UI 공용 디자인 토큰 — 색상 팔레트 / 폰트 / 패딩 / 여백.
// 일관된 시각 통일을 위해 모든 폼/탭/컨트롤이 이 클래스의 상수를 사용.

using System.Drawing.Drawing2D;

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
    public static Font MonoMd()  => new(FamilyMono, 10f);   // 로그·읽기용
    public static Font MonoLg()  => new(FamilyMono, 11f);   // md 입력 — 가독성 우선
    public static Font Hint()    => new(FamilyPrimary, 9f, FontStyle.Italic);

    // ─ 패딩/여백 ───────────────────────────────────────────────────────
    public const int Pad     = 8;
    public const int PadLg   = 16;
    public const int PadXs   = 4;
    public const int RowGap  = 6;

    public static Padding PanelPadding => new(PadLg);
    public static Padding GroupPadding => new(Pad, Pad + 4, Pad, Pad);

    // ─ 액세서리 ────────────────────────────────────────────────────────

    /// <summary>flat 스타일 버튼 (default Win10 보다 깔끔).
    /// <paramref name="glyph"/> 가 주어지면 Segoe MDL2 Assets 글리프를 좌측에
    /// 16×16 Bitmap 으로 렌더링해 Image 로 박음 ([MdlIcon] 상수 사용).</summary>
    public static void StyleFlatButton(Button b, bool accent = false, string? glyph = null)
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

        if (glyph is not null)
        {
            b.Image = RenderMdl2Glyph(glyph, accent ? Color.White : TextPrimary);
            b.ImageAlign = ContentAlignment.MiddleLeft;
            b.TextImageRelation = TextImageRelation.ImageBeforeText;
            b.Padding = new Padding(10, 4, 12, 4);
        }
    }

    /// <summary>Segoe MDL2 Assets 글리프 1 개를 size×size Bitmap 에 그려 반환.
    /// 버튼/라벨 Image 슬롯에 박아 mono-color 아이콘으로 사용.</summary>
    public static Bitmap RenderMdl2Glyph(string glyph, Color color, int size = 16)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe MDL2 Assets", size - 4, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), sf);
        return bmp;
    }

    /// <summary>TabControl 을 OwnerDraw 로 전환 — active tab 은 Accent 텍스트 +
    /// 하단 underline bar. 기본 chunky tab 비주얼 대비 모던.</summary>
    public static void StyleTabControl(TabControl tc)
    {
        tc.DrawMode = TabDrawMode.OwnerDrawFixed;
        tc.SizeMode = TabSizeMode.Fixed;
        tc.ItemSize = new Size(160, 36);
        tc.Padding = new Point(18, 8);
        tc.Font = Body();
        tc.DrawItem += (s, e) =>
        {
            if (s is not TabControl t) return;
            var selected = e.Index == t.SelectedIndex;
            var rect = e.Bounds;
            var bg = selected ? PanelBg : Background;
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, rect);

            var font = selected ? BodyBold() : Body();
            var fg = selected ? Accent : TextMuted;
            TextRenderer.DrawText(e.Graphics, t.TabPages[e.Index].Text, font, rect, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            if (selected)
            {
                var underline = new Rectangle(rect.X + 18, rect.Bottom - 3, rect.Width - 36, 2);
                using var bar = new SolidBrush(Accent);
                e.Graphics.FillRectangle(bar, underline);
            }
            font.Dispose();
        };
        tc.SelectedIndexChanged += (_, _) => tc.Invalidate();
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

/// <summary>Segoe MDL2 Assets 글리프 상수 — 이모지 대신 사용 (mono 색 일관).
/// 코드포인트 출처: docs.microsoft.com/windows/apps/design/style/segoe-ui-symbol-font.</summary>
public static class MdlIcon
{
    public const string Save        = "";   // 저장
    public const string Refresh     = "";   // 새로고침/초기화
    public const string Sync        = "";   // 변환/동기화
    public const string Folder      = "";   // 폴더 열기
    public const string OpenFile    = "";   // 파일 열기
    public const string Clear       = "";   // 지우기
    public const string Info        = "";   // 정보(?)
    public const string Switch      = "";   // 전환 (한/글 선택)
    public const string Tag         = "";   // 샘플/태그
    public const string AcceptCheck = "";   // ✓
    public const string Cancel      = "";   // ✗
}

/// <summary>둥근 상태 indicator — 8×8 px 원, BackColor 만 바꾸면 됨.
/// 폰트 ●문자보다 픽셀퍼펙트하게 그려져 다이얼/HiDPI 환경에서도 일관.</summary>
public sealed class CircularStatusDot : Panel
{
    private Color _dotColor = ForgeTheme.TextMuted;

    public Color DotColor
    {
        get => _dotColor;
        set { _dotColor = value; Invalidate(); }
    }

    public CircularStatusDot()
    {
        DoubleBuffered = true;
        Size = new Size(12, 12);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(_dotColor);
        const int pad = 1;
        e.Graphics.FillEllipse(brush, pad, pad, Width - pad * 2, Height - pad * 2);
    }
}
