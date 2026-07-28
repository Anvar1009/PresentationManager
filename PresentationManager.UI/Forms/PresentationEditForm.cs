using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Modal Add/Edit dialog. In Edit mode the file picker is optional — leaving it untouched keeps
/// the presentation's existing stored file.</summary>
public sealed class PresentationEditForm : Form
{
    private readonly bool _requireFile;

    private readonly TextBox _fullNameBox;
    private readonly TextBox _titleBox;
    private readonly TextBox _filePathBox;
    private readonly NumericUpDown _presentationMinutes;
    private readonly NumericUpDown _discussionMinutes;

    public string FullName => _fullNameBox.Text.Trim();
    public string Title => _titleBox.Text.Trim();
    public string? SelectedFilePath { get; private set; }
    public PresentationFileType? SelectedFileType { get; private set; }
    public int PresentationTimeSeconds => (int)_presentationMinutes.Value * 60;
    public int DiscussionTimeSeconds => (int)_discussionMinutes.Value * 60;

    public PresentationEditForm(string dialogTitle, bool requireFile)
    {
        _requireFile = requireFile;

        Text = dialogTitle;
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 320);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _fullNameBox = DarkTextBox();
        _titleBox = DarkTextBox();
        _filePathBox = DarkTextBox();
        _filePathBox.ReadOnly = true;
        _presentationMinutes = DarkNumericUpDown();
        _presentationMinutes.Value = 3;
        _discussionMinutes = DarkNumericUpDown();
        _discussionMinutes.Value = 2;

        var browseButton = new Button { Text = "Ko'rib chiqish...", Dock = DockStyle.Right, Width = 120, FlatStyle = FlatStyle.Flat, BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary, Font = new Font("Segoe UI", 8.5f) };
        browseButton.Click += OnBrowseClick;

        var filePanel = new Panel { Dock = DockStyle.Fill };
        filePanel.Controls.Add(_filePathBox);
        filePanel.Controls.Add(browseButton);

        AddRow(layout, 0, "Ism-familya *", _fullNameBox);
        AddRow(layout, 1, "Sarlavha *", _titleBox);
        AddRow(layout, 2, "Fayl (.ppt/.pptx/.pdf) *", filePanel);
        AddRow(layout, 3, "Taqdimot (daqiqa)", _presentationMinutes);
        AddRow(layout, 4, "Muhokama (daqiqa)", _discussionMinutes);

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

    private static NumericUpDown DarkNumericUpDown() => new()
    {
        Dock = DockStyle.Left,
        Width = 80,
        Minimum = 1,
        Maximum = 60,
        Value = 3,
        BackColor = AppColors.PanelAlt,
        ForeColor = AppColors.TextPrimary,
        BorderStyle = BorderStyle.FixedSingle
    };

    /// <summary>Pre-fills the form for Edit mode. The file field stays empty — the operator only picks a
    /// new file if they actually want to replace it.</summary>
    public void LoadFrom(Presentation presentation)
    {
        _fullNameBox.Text = presentation.FullName;
        _titleBox.Text = presentation.Title;
        _presentationMinutes.Value = Math.Clamp(presentation.PresentationTimeSeconds / 60, 1, 60);
        _discussionMinutes.Value = Math.Clamp(presentation.DiscussionTimeSeconds / 60, 1, 60);
        _filePathBox.Text = "(o'zgarmagan - almashtirish uchun tanlang)";
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Taqdimot fayllari (*.ppt;*.pptx;*.pdf)|*.ppt;*.pptx;*.pdf",
            Title = "Taqdimot faylini tanlang"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SelectedFilePath = dialog.FileName;
        _filePathBox.Text = Path.GetFileName(dialog.FileName);
        SelectedFileType = Path.GetExtension(dialog.FileName).ToLowerInvariant() == ".pdf"
            ? PresentationFileType.Pdf
            : PresentationFileType.Pptx;
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FullName) || string.IsNullOrWhiteSpace(Title))
        {
            MessageBox.Show(this, "Ism-familya va sarlavha to'ldirilishi shart.", "Tekshiruv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_requireFile && SelectedFilePath is null)
        {
            MessageBox.Show(this, "Iltimos, taqdimot faylini tanlang.", "Tekshiruv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
