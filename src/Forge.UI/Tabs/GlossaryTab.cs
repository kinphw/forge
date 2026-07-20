// 탭 ④ 상용구 — 준말(변경전) → 본말(변경후) 치환 규칙 관리.
//
// Ctrl+Shift+I (glossary_expand) 가 여기 등록된 목록을 참조해, 한/글에서 캐럿
// 바로 앞 준말을 본말로 치환한다. 편집은 즉시 %APPDATA%\Forge\settings.json 에
// 자동 저장 (Glossary.Save). 기본 5종 제공 + 사용자 자유 추가.

using Forge.Core;

namespace Forge.UI.Tabs;

public sealed class GlossaryTab : TabPage
{
    private DataGridView _grid = null!;
    private Label _status = null!;
    private bool _loading;

    public GlossaryTab()
    {
        Text = "상용구";
        BackColor = ForgeTheme.Background;
        Padding = ForgeTheme.PanelPadding;
        BuildUI();
        LoadIntoGrid();
    }

    private void BuildUI()
    {
        _grid = new DataGridView
        {
            // 한 글자짜리 준말/본말이 대부분 — 탭 전체로 늘리지 않고 왼쪽에 고정 폭으로.
            Dock = DockStyle.Left,
            Width = 430,
            BackgroundColor = ForgeTheme.PanelBg,
            BorderStyle = BorderStyle.FixedSingle,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AllowUserToResizeRows = false,
            RowHeadersVisible = true,           // 좌측 행 헤더 = 행 선택/삭제 affordance
            RowHeadersWidth = 30,
            RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            EnableHeadersVisualStyles = false,
            GridColor = ForgeTheme.Border,
            Font = ForgeTheme.Body(),
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            Margin = new Padding(0, ForgeTheme.Pad, 0, 0),
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = ForgeTheme.GroupBg;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = ForgeTheme.TextPrimary;
        _grid.ColumnHeadersDefaultCellStyle.Font = ForgeTheme.BodyBold();
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        _grid.ColumnHeadersHeight = 32;
        _grid.RowTemplate.Height = 28;
        _grid.DefaultCellStyle.SelectionBackColor = ForgeTheme.AccentLight;
        _grid.DefaultCellStyle.SelectionForeColor = ForgeTheme.TextPrimary;
        _grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);

        var colBefore = new DataGridViewTextBoxColumn
        {
            HeaderText = "변경전 (준말)",
            Width = 130,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        var colAfter = new DataGridViewTextBoxColumn
        {
            HeaderText = "변경후 (본말)",   // 본말은 긴 상용구도 가능 — 준말보다 넉넉히
            Width = 240,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        _grid.Columns.Add(colBefore);
        _grid.Columns.Add(colAfter);

        _grid.CellEndEdit += (_, _) => Persist();
        _grid.RowsRemoved += (_, _) => Persist();

        // Dock 스택 — Fill(grid) 먼저 추가 후 Top 들 (마지막 추가가 최상단)
        var toolbar = BuildToolbar();
        toolbar.Dock = DockStyle.Top;

        var desc = ForgeTheme.SectionDesc(
            "한/글에서 준말을 입력하고 바로 뒤에서 Ctrl+Shift+I 를 누르면 본말로 치환됩니다.\n" +
            "예: 'ㅈ' 입력 후 Ctrl+Shift+I → '§'. 편집은 즉시 자동 저장됩니다.");
        desc.Dock = DockStyle.Top;

        var title = ForgeTheme.SectionTitle("상용구 — 준말 → 본말 치환 (Ctrl+Shift+I)");
        title.Dock = DockStyle.Top;

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(desc);
        Controls.Add(title);
    }

    private Panel BuildToolbar()
    {
        var bar = new Panel { Height = 44, Margin = new Padding(0, 0, 0, ForgeTheme.Pad) };

        var addBtn = new Button { Text = "행 추가" };
        ForgeTheme.StyleFlatButton(addBtn, glyph: MdlIcon.Add);
        addBtn.Location = new Point(0, 6);
        addBtn.Click += (_, _) =>
        {
            int idx = _grid.Rows.Add();
            _grid.CurrentCell = _grid.Rows[idx].Cells[0];
            _grid.BeginEdit(true);
        };
        bar.Controls.Add(addBtn);

        var delBtn = new Button { Text = "행 삭제" };
        ForgeTheme.StyleFlatButton(delBtn, glyph: MdlIcon.Delete);
        delBtn.Location = new Point(120, 6);
        delBtn.Click += (_, _) =>
        {
            if (_grid.CurrentRow is { IsNewRow: false } row)
            {
                _grid.Rows.Remove(row);   // RowsRemoved → Persist
            }
        };
        bar.Controls.Add(delBtn);

        var restoreBtn = new Button { Text = "기본값 복원" };
        ForgeTheme.StyleFlatButton(restoreBtn, glyph: MdlIcon.Refresh);
        restoreBtn.Location = new Point(240, 6);
        restoreBtn.Click += (_, _) =>
        {
            var dr = MessageBox.Show(FindForm(),
                "상용구를 기본 5종(· § → □ ◦)으로 되돌립니다.\n현재 목록은 덮어써집니다. 계속하시겠습니까?",
                "기본값 복원", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (dr != DialogResult.OK) return;
            Glossary.ResetToDefaults();
            LoadIntoGrid();
        };
        bar.Controls.Add(restoreBtn);

        _status = new Label
        {
            AutoSize = true,
            ForeColor = ForgeTheme.TextMuted,
            Font = ForgeTheme.Small(),
            Location = new Point(380, 14),
            Text = "",
        };
        bar.Controls.Add(_status);

        return bar;
    }

    private void LoadIntoGrid()
    {
        _loading = true;
        _grid.Rows.Clear();
        foreach (var e in Glossary.Load())
            _grid.Rows.Add(e.Before, e.After);
        _loading = false;
        UpdateStatus("로드됨");
    }

    private void Persist()
    {
        if (_loading) return;
        var entries = new List<GlossaryEntry>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string before = (row.Cells[0].Value as string ?? "").Trim();
            string after = row.Cells[1].Value as string ?? "";
            if (before.Length == 0) continue;
            entries.Add(new GlossaryEntry(before, after));
        }
        Glossary.Save(entries);
        UpdateStatus("자동 저장됨");
    }

    private void UpdateStatus(string prefix)
    {
        int count = _grid.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow);
        if (_status is not null) _status.Text = $"{prefix} · {count}개";
    }
}
