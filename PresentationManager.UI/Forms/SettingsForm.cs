using PresentationManager.Domain.Entities;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

public sealed class SettingsForm : Form
{
    private readonly NumericUpDown _presentationMinutes;
    private readonly NumericUpDown _discussionMinutes;
    private readonly CheckBox _autoNextCheck;
    private readonly CheckBox _fullscreenCheck;
    private readonly CheckBox _alarmEnabledCheck;
    private readonly TextBox _alarmPathBox;

    private readonly string _storageFolderPath;

    public AppSettings Result { get; private set; } = null!;

    public SettingsForm(AppSettings current)
    {
        _storageFolderPath = current.StorageFolderPath;

        Text = "Sozlamalar";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(440, 340);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _presentationMinutes = new NumericUpDown
        {
            Dock = DockStyle.Left, Width = 80, Minimum = 1, Maximum = 60,
            Value = Math.Clamp(current.DefaultPresentationTimeSeconds / 60, 1, 60),
            BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary, BorderStyle = BorderStyle.FixedSingle
        };
        _discussionMinutes = new NumericUpDown
        {
            Dock = DockStyle.Left, Width = 80, Minimum = 1, Maximum = 60,
            Value = Math.Clamp(current.DefaultDiscussionTimeSeconds / 60, 1, 60),
            BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary, BorderStyle = BorderStyle.FixedSingle
        };
        _autoNextCheck = new CheckBox { Dock = DockStyle.Left, AutoSize = true, Checked = current.AutoNext, Text = "Muhokama vaqti tugaganda avtomatik keyingisiga o'tish" };
        _fullscreenCheck = new CheckBox { Dock = DockStyle.Left, AutoSize = true, Checked = current.FullscreenEnabled, Text = "Ikkinchi monitorda to'liq ekran" };
        _alarmEnabledCheck = new CheckBox { Dock = DockStyle.Left, AutoSize = true, Checked = current.AlarmEnabled, Text = "Vaqt tugaganda ovoz chalish" };

        _alarmPathBox = new TextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, Text = current.AlarmSoundPath ?? "(standart tizim ovozi)",
            BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary, BorderStyle = BorderStyle.FixedSingle
        };
        var browseButton = new Button { Text = "Ko'rib chiqish...", Dock = DockStyle.Right, Width = 120, FlatStyle = FlatStyle.Flat, BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary, Font = new Font("Segoe UI", 8.5f) };
        browseButton.Click += OnBrowseAlarmClick;
        var alarmPanel = new Panel { Dock = DockStyle.Fill };
        alarmPanel.Controls.Add(_alarmPathBox);
        alarmPanel.Controls.Add(browseButton);

        AddRow(layout, 0, "Taqdimot vaqti (daqiqa)", _presentationMinutes);
        AddRow(layout, 1, "Muhokama vaqti (daqiqa)", _discussionMinutes);
        AddRow(layout, 2, "Avtomatik o'tish", _autoNextCheck);
        AddRow(layout, 3, "To'liq ekran", _fullscreenCheck);
        AddRow(layout, 4, "Signal", _alarmEnabledCheck);
        AddRow(layout, 5, "Signal ovoz fayli", alarmPanel);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelButton = new Button { Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 90, FlatStyle = FlatStyle.Flat, BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary };
        var saveButton = new Button { Text = "Saqlash", Width = 90, FlatStyle = FlatStyle.Flat, BackColor = AppColors.Accent, ForeColor = AppColors.TextPrimary };
        saveButton.Click += OnSaveClick;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        layout.Controls.Add(buttonPanel, 0, 6);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string labelText, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(new Label { Text = labelText, ForeColor = AppColors.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private void OnBrowseAlarmClick(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "Ovoz fayllari (*.wav)|*.wav", Title = "Signal ovozini tanlang" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _alarmPathBox.Text = dialog.FileName;
        }
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        Result = new AppSettings
        {
            Id = 1,
            DefaultPresentationTimeSeconds = (int)_presentationMinutes.Value * 60,
            DefaultDiscussionTimeSeconds = (int)_discussionMinutes.Value * 60,
            AutoNext = _autoNextCheck.Checked,
            Theme = "Dark",
            FullscreenEnabled = _fullscreenCheck.Checked,
            AlarmSoundPath = _alarmPathBox.Text.StartsWith('(') ? null : _alarmPathBox.Text,
            AlarmEnabled = _alarmEnabledCheck.Checked,
            StorageFolderPath = _storageFolderPath
        };
        DialogResult = DialogResult.OK;
    }
}
