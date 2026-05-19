// 다중 한/글 인스턴스 선택 다이얼로그.
// Python 원본 forge/ui/hwp_picker.py 등가.

using Forge.Core;

namespace Forge.UI;

public sealed class HwpPickerForm : Form
{
    public HwpInstance? Selected { get; private set; }

    public HwpPickerForm(IReadOnlyList<HwpInstance> instances)
    {
        Text = "한/글 인스턴스 선택";
        ClientSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10),
        };
        foreach (var inst in instances)
            listBox.Items.Add(inst);
        listBox.DisplayMember = nameof(HwpInstance.DisplayLabel);
        if (instances.Count > 0) listBox.SelectedIndex = 0;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        var okBtn = new Button { Text = "선택", DialogResult = DialogResult.OK, Width = 80 };
        var cancelBtn = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 80 };
        bottom.Controls.Add(okBtn);
        bottom.Controls.Add(cancelBtn);

        void RepositionButtons()
        {
            okBtn.Location = new Point(bottom.Width - 180, 8);
            cancelBtn.Location = new Point(bottom.Width - 92, 8);
        }
        bottom.Resize += (_, _) => RepositionButtons();
        RepositionButtons();

        okBtn.Click += (_, _) =>
        {
            Selected = listBox.SelectedItem as HwpInstance;
            DialogResult = DialogResult.OK;
            Close();
        };
        listBox.DoubleClick += (_, _) => okBtn.PerformClick();

        Controls.Add(listBox);
        Controls.Add(bottom);
        AcceptButton = okBtn;
        CancelButton = cancelBtn;
    }
}
