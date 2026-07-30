using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Modal "Yangi loyiha" dialog — captures the event's day(s), an optional time, and an optional
/// location alongside the project's name, so presenters can later be told when/where via the Telegram bot's
/// post-upload reminder (see <c>PresentationBotHostedService</c>).</summary>
public sealed class ProjectEditForm : Form
{
    private readonly TextBox _nameBox;
    private readonly DateTimePicker _startDatePicker;
    private readonly DateTimePicker _endDatePicker;
    private readonly CheckBox _hasTimeCheck;
    private readonly DateTimePicker _timePicker;
    private readonly TextBox _locationBox;

    public string ProjectName => _nameBox.Text.Trim();
    public DateOnly EventStartDate => DateOnly.FromDateTime(_startDatePicker.Value.Date);
    public DateOnly EventEndDate => DateOnly.FromDateTime(_endDatePicker.Value.Date);
    public TimeOnly? EventTime => _hasTimeCheck.Checked ? TimeOnly.FromTimeSpan(_timePicker.Value.TimeOfDay) : null;
    public string? Location => string.IsNullOrWhiteSpace(_locationBox.Text) ? null : _locationBox.Text.Trim();

    public ProjectEditForm()
    {
        Text = "Yangi loyiha";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 340);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _nameBox = DarkTextBox();

        _startDatePicker = DarkDatePicker();
        _endDatePicker = DarkDatePicker();
        // Keeps the end date from ever being dragged before the start date instead of only catching it on
        // Save - EventEndDate < EventStartDate is exactly what ProjectService.CreateAsync rejects.
        _startDatePicker.ValueChanged += (_, _) =>
        {
            if (_endDatePicker.Value < _startDatePicker.Value)
            {
                _endDatePicker.Value = _startDatePicker.Value;
            }
        };

        _hasTimeCheck = new CheckBox { Dock = DockStyle.Left, AutoSize = true, Text = "Belgilash" };
        _timePicker = DarkDatePicker();
        _timePicker.Format = DateTimePickerFormat.Time;
        _timePicker.ShowUpDown = true;
        _timePicker.Enabled = false;
        _hasTimeCheck.CheckedChanged += (_, _) => _timePicker.Enabled = _hasTimeCheck.Checked;

        var timePanel = new Panel { Dock = DockStyle.Fill };
        _timePicker.Dock = DockStyle.Fill;
        var timeCheckWrap = new Panel { Dock = DockStyle.Left, Width = 90 };
        timeCheckWrap.Controls.Add(_hasTimeCheck);
        timePanel.Controls.Add(_timePicker);
        timePanel.Controls.Add(timeCheckWrap);

        _locationBox = DarkTextBox();

        AddRow(layout, 0, "Nomi *", _nameBox);
        AddRow(layout, 1, "Boshlanish sanasi *", _startDatePicker);
        AddRow(layout, 2, "Tugash sanasi *", _endDatePicker);
        AddRow(layout, 3, "Vaqti", timePanel);
        AddRow(layout, 4, "Manzili", _locationBox);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelButton = new Button { Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 90, FlatStyle = FlatStyle.Flat, BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary };
        var saveButton = new Button { Text = "Saqlash", Width = 90, FlatStyle = FlatStyle.Flat, BackColor = AppColors.Accent, ForeColor = AppColors.TextPrimary };
        saveButton.Click += OnSaveClick;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        layout.Controls.Add(buttonPanel, 0, 5);
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

    private static TextBox DarkTextBox() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = AppColors.PanelAlt,
        ForeColor = AppColors.TextPrimary,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static DateTimePicker DarkDatePicker() => new()
    {
        Dock = DockStyle.Fill,
        Format = DateTimePickerFormat.Short,
        CalendarForeColor = AppColors.TextPrimary,
        CalendarMonthBackground = AppColors.PanelAlt
    };

    private void OnSaveClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            MessageBox.Show(this, "Loyiha nomi to'ldirilishi shart.", "Tekshiruv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (EventEndDate < EventStartDate)
        {
            MessageBox.Show(this, "Tugash sanasi boshlanish sanasidan oldin bo'lishi mumkin emas.", "Tekshiruv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
