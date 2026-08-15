using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Admin-only "Ishtirokchilar" dialog for one project. A person must already have started the
/// Telegram bot and shared their contact (becoming a registered <see cref="Presenter"/>) before Admin can
/// approve them here — <see cref="OnAssignClick"/> picks from that already-registered list rather than
/// typing a phone number blind, so the resulting approval is immediately linked and the bot pushes them a
/// notification right away, and only from that point on can they upload a presentation for this project (see
/// <c>PresenterAssignmentsController.Add</c> / <c>PresentationBotHostedService.ShowAssignedProjectsOrWaitAsync</c>).</summary>
public sealed class PresenterAssignmentManagementForm : Form
{
    private readonly PresenterAssignmentService _assignmentService;
    private readonly IPresenterRepository _presenterRepository;
    private readonly int _projectId;
    private readonly ListBox _listBox;
    private List<PresenterProjectAssignment> _assignments = [];
    private Dictionary<int, Presenter> _presentersById = [];

    public PresenterAssignmentManagementForm(PresenterAssignmentService assignmentService, IPresenterRepository presenterRepository, int projectId, string projectName)
    {
        _assignmentService = assignmentService;
        _presenterRepository = presenterRepository;
        _projectId = projectId;

        Text = $"Ishtirokchilar - {projectName}";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 420);

        var header = new Label
        {
            Text = "Faqat botga /start bosib kontaktini ulashgan odamlar biriktirilishi mumkin",
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(16, 10, 16, 0),
            ForeColor = LightColors.TextSecondary,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic)
        };

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

        var toolbarWrap = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(16, 0, 16, 8) };
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        var assignButton = new Button { Text = "+ Ishtirokchi biriktirish", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0), FlatStyle = FlatStyle.Flat, BackColor = LightColors.Success, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        assignButton.Click += OnAssignClick;
        var deleteButton = new Button { Text = "O'chirish", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = LightColors.Danger, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        deleteButton.Click += OnDeleteClick;
        toolbar.Controls.Add(assignButton, 0, 0);
        toolbar.Controls.Add(deleteButton, 1, 0);
        toolbarWrap.Controls.Add(toolbar);

        var closeButton = new Button { Text = "Yopish", DialogResult = DialogResult.Cancel, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary };
        var closeWrap = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(16, 0, 16, 12) };
        closeWrap.Controls.Add(closeButton);

        Controls.Add(listWrap);
        Controls.Add(toolbarWrap);
        Controls.Add(closeWrap);
        Controls.Add(header);
        CancelButton = closeButton;

        Load += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _assignments = await _assignmentService.GetByProjectIdAsync(_projectId);
        var presenters = await _presenterRepository.GetAllAsync();
        _presentersById = presenters.ToDictionary(p => p.Id);

        _listBox.BeginUpdate();
        _listBox.Items.Clear();
        foreach (var assignment in _assignments)
        {
            var presenter = _presentersById.GetValueOrDefault(assignment.PresenterId);
            var name = presenter is null ? "(topilmadi)" : presenter.FullName;
            var phone = presenter?.PhoneNumber ?? "-";
            _listBox.Items.Add($"{name} - {phone}");
        }
        _listBox.EndUpdate();
    }

    private PresenterProjectAssignment? GetSelected()
    {
        var index = _listBox.SelectedIndex;
        return index >= 0 && index < _assignments.Count ? _assignments[index] : null;
    }

    private async void OnAssignClick(object? sender, EventArgs e)
    {
        var registered = await _presenterRepository.GetAllAsync();
        var alreadyAssignedIds = _assignments.Select(a => a.PresenterId).ToHashSet();
        var candidates = registered.Where(p => !alreadyAssignedIds.Contains(p.Id)).ToList();

        if (candidates.Count == 0)
        {
            MessageBox.Show(this,
                "Hozircha botga ro'yxatdan o'tgan (va shu loyihaga hali biriktirilmagan) odam yo'q. Avval kerakli odam botga /start bosib kontaktini ulashsin.",
                "Ishtirokchi topilmadi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new PresenterAssignForm(candidates);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedPresenterId is not { } presenterId)
        {
            return;
        }

        try
        {
            await _assignmentService.AssignAsync(_projectId, presenterId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Biriktirishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnDeleteClick(object? sender, EventArgs e)
    {
        var selected = GetSelected();
        if (selected is null)
        {
            return;
        }

        var name = _presentersById.GetValueOrDefault(selected.PresenterId)?.FullName ?? "Bu ishtirokchi";
        var confirm = MessageBox.Show(this, $"'{name}' ushbu loyihadan chiqarilsinmi?", "O'chirishni tasdiqlash", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _assignmentService.DeleteAsync(selected.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "O'chirishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>Picks one already bot-registered person to approve for a project — used by
/// <see cref="PresenterAssignmentManagementForm"/>.</summary>
public sealed class PresenterAssignForm : Form
{
    private readonly List<Presenter> _candidates;
    private readonly ListBox _listBox;
    private readonly TextBox _searchBox;

    public int? SelectedPresenterId { get; private set; }

    public PresenterAssignForm(List<Presenter> candidates)
    {
        _candidates = candidates;

        Text = "Ishtirokchi biriktirish";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 420);

        _searchBox = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = "Ism yoki telefon bo'yicha qidirish...",
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        _searchBox.TextChanged += (_, _) => PopulateList();
        var searchWrap = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(16, 12, 16, 0) };
        searchWrap.Controls.Add(_searchBox);

        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 10.5f)
        };
        _listBox.DoubleClick += (_, _) => TryAccept();
        var listWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 4) };
        listWrap.Controls.Add(_listBox);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(16, 0, 16, 16), FlowDirection = FlowDirection.RightToLeft };
        var cancelButton = new Button { Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary };
        var assignButton = new Button { Text = "Biriktirish", Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.Accent, ForeColor = Color.White };
        assignButton.Click += (_, _) => TryAccept();
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(assignButton);

        Controls.Add(listWrap);
        Controls.Add(buttonPanel);
        Controls.Add(searchWrap);
        CancelButton = cancelButton;

        PopulateList();
    }

    private List<Presenter> _displayed = [];

    private void PopulateList()
    {
        var filter = _searchBox.Text.Trim();
        _displayed = string.IsNullOrEmpty(filter)
            ? _candidates
            : _candidates.Where(p =>
                    p.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (p.PhoneNumber?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

        _listBox.BeginUpdate();
        _listBox.Items.Clear();
        foreach (var presenter in _displayed)
        {
            _listBox.Items.Add($"{presenter.FullName} - {presenter.PhoneNumber}");
        }
        _listBox.EndUpdate();
    }

    private void TryAccept()
    {
        var index = _listBox.SelectedIndex;
        if (index < 0 || index >= _displayed.Count)
        {
            return;
        }

        SelectedPresenterId = _displayed[index].Id;
        DialogResult = DialogResult.OK;
    }
}
