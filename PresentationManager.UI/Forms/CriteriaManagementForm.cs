using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Admin-only "Baholash mezonlari" dialog for one project — add/delete the dynamic criteria judges
/// score against via the Telegram bot (see <c>PresentationBotHostedService</c>'s judge flow).</summary>
public sealed class CriteriaManagementForm : Form
{
    private readonly CriterionService _criterionService;
    private readonly int _projectId;
    private readonly ListBox _listBox;
    private List<EvaluationCriterion> _criteria = [];

    public CriteriaManagementForm(CriterionService criterionService, int projectId, string projectName)
    {
        _criterionService = criterionService;
        _projectId = projectId;

        Text = $"Baholash mezonlari - {projectName}";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 420);

        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.PanelAlt,
            ForeColor = AppColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 10.5f)
        };
        var listWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 16, 4) };
        listWrap.Controls.Add(_listBox);

        var toolbarWrap = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(16, 0, 16, 8) };
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        var addButton = new Button { Text = "+ Mezon qo'shish", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0), FlatStyle = FlatStyle.Flat, BackColor = AppColors.Success, ForeColor = AppColors.Background, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        addButton.Click += OnAddClick;
        var deleteButton = new Button { Text = "O'chirish", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = AppColors.Danger, ForeColor = AppColors.TextPrimary, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        deleteButton.Click += OnDeleteClick;
        toolbar.Controls.Add(addButton, 0, 0);
        toolbar.Controls.Add(deleteButton, 1, 0);
        toolbarWrap.Controls.Add(toolbar);

        var closeButton = new Button { Text = "Yopish", DialogResult = DialogResult.Cancel, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary };
        var closeWrap = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(16, 0, 16, 12) };
        closeWrap.Controls.Add(closeButton);

        Controls.Add(listWrap);
        Controls.Add(toolbarWrap);
        Controls.Add(closeWrap);
        CancelButton = closeButton;

        Load += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _criteria = await _criterionService.GetByProjectIdAsync(_projectId);

        _listBox.BeginUpdate();
        _listBox.Items.Clear();
        foreach (var criterion in _criteria)
        {
            _listBox.Items.Add($"{criterion.Name}  (max {criterion.MaxScore})");
        }
        _listBox.EndUpdate();
    }

    private EvaluationCriterion? GetSelected()
    {
        var index = _listBox.SelectedIndex;
        return index >= 0 && index < _criteria.Count ? _criteria[index] : null;
    }

    private async void OnAddClick(object? sender, EventArgs e)
    {
        using var dialog = new CriterionEditForm();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _criterionService.CreateAsync(_projectId, dialog.CriterionName, dialog.MaxScore);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Mezon qo'shishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnDeleteClick(object? sender, EventArgs e)
    {
        var selected = GetSelected();
        if (selected is null)
        {
            return;
        }

        var confirm = MessageBox.Show(this, $"'{selected.Name}' mezoni o'chirilsinmi? Bu mezon bo'yicha qo'yilgan barcha ballar ham o'chadi.",
            "O'chirishni tasdiqlash", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _criterionService.DeleteAsync(selected.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "O'chirishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>Small "Nomi + Maksimal ball" prompt used by <see cref="CriteriaManagementForm"/>.</summary>
public sealed class CriterionEditForm : Form
{
    private readonly TextBox _nameBox;
    private readonly NumericUpDown _maxScoreBox;

    public string CriterionName => _nameBox.Text.Trim();
    public int MaxScore => (int)_maxScoreBox.Value;

    public CriterionEditForm()
    {
        Text = "Yangi mezon";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 170);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _nameBox = new TextBox { Dock = DockStyle.Fill, BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
        _maxScoreBox = new NumericUpDown { Dock = DockStyle.Left, Width = 80, Minimum = 1, Maximum = 1000, Value = 10, BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary, BorderStyle = BorderStyle.FixedSingle };

        AddRow(layout, 0, "Nomi *", _nameBox);
        AddRow(layout, 1, "Maksimal ball *", _maxScoreBox);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelButton = new Button { Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 90, FlatStyle = FlatStyle.Flat, BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary };
        var saveButton = new Button { Text = "Saqlash", Width = 90, FlatStyle = FlatStyle.Flat, BackColor = AppColors.Accent, ForeColor = AppColors.TextPrimary };
        saveButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(CriterionName))
            {
                MessageBox.Show(this, "Mezon nomi to'ldirilishi shart.", "Tekshiruv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        layout.Controls.Add(buttonPanel, 0, 2);
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
}
