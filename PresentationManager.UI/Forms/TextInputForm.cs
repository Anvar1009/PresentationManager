using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Small single-field text prompt — used wherever a dialog only needs one short string back (e.g.
/// naming a new project), so a full bespoke form isn't needed for that.</summary>
public sealed class TextInputForm : Form
{
    private readonly TextBox _valueBox;

    public string Value => _valueBox.Text.Trim();

    public TextInputForm(string dialogTitle, string labelText, string initialValue = "")
    {
        Text = dialogTitle;
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 130);

        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(16, 12, 16, 0),
            ForeColor = AppColors.TextSecondary
        };

        _valueBox = new TextBox
        {
            Dock = DockStyle.Top,
            Text = initialValue,
            Margin = new Padding(16, 0, 16, 0),
            BackColor = AppColors.PanelAlt,
            ForeColor = AppColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        var fieldWrap = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(16, 4, 16, 0) };
        fieldWrap.Controls.Add(_valueBox);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 8, 16, 8) };
        var cancelButton = new Button { Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 90, FlatStyle = FlatStyle.Flat, BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary };
        var saveButton = new Button { Text = "Saqlash", Width = 90, FlatStyle = FlatStyle.Flat, BackColor = AppColors.Accent, ForeColor = AppColors.TextPrimary };
        saveButton.Click += OnSaveClick;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);

        Controls.Add(fieldWrap);
        Controls.Add(label);
        Controls.Add(buttonPanel);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            MessageBox.Show(this, "Maydon bo'sh bo'lishi mumkin emas.", "Tekshiruv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
