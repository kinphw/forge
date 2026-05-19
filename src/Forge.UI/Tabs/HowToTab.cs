// 탭 ⓪ How to? — 정적 안내. 코드 분기 없음.
// Python 원본 forge/ui/tabs/howto_tab.py 1:1.

namespace Forge.UI.Tabs;

public sealed class HowToTab : TabPage
{
    private static readonly (string Tab, string Desc)[] TabRows =
    {
        ("① 개별 작업", "한/글 활성 문서에 룰 1개씩 적용 (단축키 또는 버튼)"),
        ("② 양식삽입", "보고서 양식 11종을 커서 위치에 1-클릭 삽입"),
        ("③ 마크다운", "개조식 markdown → 새 .hwpx 파일로 변환 (배치)"),
    };

    // Forge 는 표준 markdown `#`/`##` 헤더 미사용 — 개조식 글머리 + callout.
    private static readonly (string Key, string Desc)[] MdRows =
    {
        ("보고서명: ...",       "대제목 — YAML front-matter (`---` 사이). `#` 헤더 아님"),
        ("1.  2.  3. ...",      "섹션 헤더"),
        ("가.  나.  다. ...",   "소제목"),
        ("□ / ○ / - / ·",       "본문 글머리 4단계 (왼쪽이 상위)"),
        ("□ (요약) ...",        "요약 강조 — HY울릉도M 폰트"),
        ("* / ** / ***",        "참조 주석 (별 개수로 단계)"),
        ("※ / †",               "일반 주석"),
        ("=> ...",              "결론 박스"),
        ("[참고]",              "참고 callout (다음 빈 줄까지 본문)"),
        ("[붙임] / [붙임 N]",   "붙임 — 자동 페이지 break"),
        ("__강조__",            "인라인 Bold"),
    };

    public HowToTab()
    {
        BackColor = ForgeTheme.Background;
        Padding = ForgeTheme.PanelPadding;
        BuildUI();
    }

    private void BuildUI()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ForgeTheme.Background };
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        scroll.Controls.Add(flow);
        Controls.Add(scroll);

        flow.Controls.Add(ForgeTheme.SectionTitle("How to?"));
        flow.Controls.Add(ForgeTheme.SectionDesc(
            "한/글을 먼저 실행해 두면 Forge 가 자동 attach 합니다."));

        flow.Controls.Add(BuildSection("3 탭 구성", TabRows, mono: false));
        flow.Controls.Add(BuildSection("탭 ③ 마크다운 문법 — 개조식 (`#` 헤더 미사용)", MdRows, mono: true));

        flow.Controls.Add(new Label
        {
            Text = "자세한 사용법: 우상단 ? 버튼 / README / spec/",
            Font = ForgeTheme.Small(),
            ForeColor = ForgeTheme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, ForgeTheme.Pad, 0, 0),
        });
    }

    private static GroupBox BuildSection(string title, (string Key, string Desc)[] rows, bool mono)
    {
        var box = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, ForgeTheme.Pad),
            MinimumSize = new Size(800, 0),
        };
        ForgeTheme.StyleGroup(box);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var keyFont = mono ? ForgeTheme.Mono() : ForgeTheme.BodyBold();
        for (int i = 0; i < rows.Length; i++)
        {
            var (key, desc) = rows[i];
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label
            {
                Text = key,
                Font = keyFont,
                ForeColor = ForgeTheme.Accent,
                AutoSize = true,
                Margin = new Padding(0, 4, 12, 4),
            }, 0, i);
            grid.Controls.Add(new Label
            {
                Text = desc,
                Font = ForgeTheme.Body(),
                ForeColor = ForgeTheme.TextPrimary,
                AutoSize = true,
                MaximumSize = new Size(540, 0),
                Margin = new Padding(0, 4, 0, 4),
            }, 1, i);
        }
        box.Controls.Add(grid);
        return box;
    }
}
