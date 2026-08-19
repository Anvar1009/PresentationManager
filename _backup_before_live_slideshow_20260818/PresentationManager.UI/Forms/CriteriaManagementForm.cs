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
    private readonly TextBox _searchBox;

    /// <summary>Mirrors <see cref="_listBox"/>'s items in the same order - what a selected row resolves back
    /// to. <see cref="_allCriteria"/> is the unfiltered source <see cref="ApplyFilter"/> re-applies on every
    /// <see cref="_searchBox"/> keystroke without re-fetching.</summary>
    private List<EvaluationCriterion> _criteria = [];
    private List<EvaluationCriterion> _allCriteria = [];

    public CriteriaManagementForm(CriterionService criterionService, int projectId, string projectName)
    {
        _criterionService = criterionService;
        _projectId = projectId;

        Text = $"Baholash mezonlari - {projectName}";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 420);

        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 10.5f)
        };
        ListBoxTheme.ApplyRowDividers(_listBox);
        var listWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 4) };
        listWrap.Controls.Add(_listBox);

        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Nomi bo'yicha qidirish...",
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        _searchBox.TextChanged += (_, _) => ApplyFilter();

        // One row shared with the action buttons below, not a full-width strip of its own: the search box
        // is fixed-width on the left, buttons split the remaining space on the right.
        var toolbarWrap = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(16, 12, 16, 0) };
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        // Pinned to Percent(100), not left as the implicit AutoSize default: an AutoSize row lets the
        // TableLayoutPanel grow it to whatever height a Dock=Fill Button's GetPreferredSize reports once the
        // column is too narrow for its text on one line (word-wrap consideration) - the row then balloons
        // past toolbarWrap's fixed height and the button's vertically-centered text renders below the
        // visible, clipped area (paints, just invisible). Percent(100) keeps the row pinned to the
        // container's real height regardless of what children would prefer.
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        var searchWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0) };
        searchWrap.Controls.Add(_searchBox);
        var addButton = new Button { Text = "+ Mezon qo'shish", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0), FlatStyle = FlatStyle.Flat, BackColor = LightColors.Success, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        addButton.Click += OnAddClick;
        var deleteButton = new Button { Text = "O'chirish", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = LightColors.Danger, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        deleteButton.Click += OnDeleteClick;
        toolbar.Controls.Add(searchWrap, 0, 0);
        toolbar.Controls.Add(addButton, 1, 0);
        toolbar.Controls.Add(deleteButton, 2, 0);
        toolbarWrap.Controls.Add(toolbar);

        var closeButton = new Button { Text = "Yopish", DialogResult = DialogResult.Cancel, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary };
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
        _allCriteria = await _criterionService.GetByProjectIdAsync(_projectId);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = _searchBox.Text.Trim();
        _criteria = string.IsNullOrEmpty(filter)
            ? _allCriteria
            : _allCriteria.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

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
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 170);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _nameBox = new TextBox { Dock = DockStyle.Fill, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
        _maxScoreBox = new NumericUpDown { Dock = DockStyle.Left, Width = 80, Minimum = 1, Maximum = 1000, Value = 10, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, BorderStyle = BorderStyle.FixedSingle };

        AddRow(layout, 0, "Nomi *", _nameBox);
        AddRow(layout, 1, "Maksimal ball *", _maxScoreBox);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelButton = new Button { Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary };
        var saveButton = new Button { Text = "Saqlash", Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.Accent, ForeColor = Color.White };
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
        layout.Controls.Add(new Label { Text = labelText, ForeColor = LightColors.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}
