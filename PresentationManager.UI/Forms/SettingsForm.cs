using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.TelegramBot;
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
    private readonly AdminLinkService _adminLinkService;
    private readonly PresentationBotOptions _botOptions;
    private readonly int? _currentUserId;

    public AppSettings Result { get; private set; } = null!;

    public SettingsForm(AppSettings current, AdminLinkService adminLinkService, PresentationBotOptions botOptions, int? currentUserId)
    {
        _storageFolderPath = current.StorageFolderPath;
        _adminLinkService = adminLinkService;
        _botOptions = botOptions;
        _currentUserId = currentUserId;

        Text = "Sozlamalar";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 420);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(20)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _presentationMinutes = new NumericUpDown
        {
            Dock = DockStyle.Left, Width = 80, Minimum = 1, Maximum = 60,
            Value = Math.Clamp(current.DefaultPresentationTimeSeconds / 60, 1, 60),
            BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, BorderStyle = BorderStyle.FixedSingle
        };
        _discussionMinutes = new NumericUpDown
        {
            Dock = DockStyle.Left, Width = 80, Minimum = 1, Maximum = 60,
            Value = Math.Clamp(current.DefaultDiscussionTimeSeconds / 60, 1, 60),
            BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, BorderStyle = BorderStyle.FixedSingle
        };
        _autoNextCheck = new CheckBox { Dock = DockStyle.Left, AutoSize = true, Checked = current.AutoNext, Text = "Muhokama vaqti tugaganda avtomatik keyingisiga o'tish" };
        _fullscreenCheck = new CheckBox { Dock = DockStyle.Left, AutoSize = true, Checked = current.FullscreenEnabled, Text = "Ikkinchi monitorda to'liq ekran" };
        _alarmEnabledCheck = new CheckBox { Dock = DockStyle.Left, AutoSize = true, Checked = current.AlarmEnabled, Text = "Vaqt tugaganda ovoz chalish" };

        _alarmPathBox = new TextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, Text = current.AlarmSoundPath ?? "(standart tizim ovozi)",
            BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, BorderStyle = BorderStyle.FixedSingle
        };
        // A wrapper (not Margin - a plain Panel ignores its Dock children's Margin) so the button doesn't
        // sit flush against the textbox with no visible gap between them.
        var browseButtonWrap = new Panel { Dock = DockStyle.Right, Width = 132, Padding = new Padding(12, 0, 0, 0) };
        var browseButton = new Button
        {
            Text = "Ko'rib chiqish...", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand
        };
        browseButton.FlatAppearance.BorderColor = LightColors.Border;
        browseButton.Click += OnBrowseAlarmClick;
        browseButtonWrap.Controls.Add(browseButton);
        var alarmPanel = new Panel { Dock = DockStyle.Fill };
        alarmPanel.Controls.Add(_alarmPathBox);
        alarmPanel.Controls.Add(browseButtonWrap);

        var linkBotButton = new Button
        {
            Text = "🤖 Botga ulash", Dock = DockStyle.Left, Width = 180, FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        linkBotButton.FlatAppearance.BorderColor = LightColors.Border;
        linkBotButton.Click += OnLinkBotClick;

        AddRow(layout, 0, "Taqdimot vaqti (daqiqa)", _presentationMinutes);
        AddRow(layout, 1, "Muhokama vaqti (daqiqa)", _discussionMinutes);
        AddRow(layout, 2, "Avtomatik o'tish", _autoNextCheck);
        AddRow(layout, 3, "To'liq ekran", _fullscreenCheck);
        AddRow(layout, 4, "Signal", _alarmEnabledCheck);
        AddRow(layout, 5, "Signal ovoz fayli", alarmPanel);
        AddRow(layout, 6, "Telegram bot", linkBotButton);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 0) };
        var cancelButton = new Button
        {
            Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 100, Height = 36,
            Margin = new Padding(6, 0, 0, 0), FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, Cursor = Cursors.Hand
        };
        cancelButton.FlatAppearance.BorderColor = LightColors.Border;
        var saveButton = new Button
        {
            Text = "Saqlash", Width = 100, Height = 36, Margin = new Padding(6, 0, 6, 0),
            FlatStyle = FlatStyle.Flat, BackColor = LightColors.Accent, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.Click += OnSaveClick;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        layout.Controls.Add(buttonPanel, 0, 7);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string labelText, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(new Label { Text = labelText, ForeColor = LightColors.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    /// <summary>Same one-time deep-link mechanism as <c>AdminPanelForm</c>'s "Botga ulash" (see
    /// <see cref="BotLinkHelper"/>) - links this Operator's account so "Parolni unutdingizmi?" has a Telegram
    /// chat to deliver reset codes to (Operators get no bot-side menu of their own from this, unlike Admin).</summary>
    private void OnLinkBotClick(object? sender, EventArgs e) =>
        BotLinkHelper.ShowLinkDialog(this, _adminLinkService, _botOptions, _currentUserId);

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
