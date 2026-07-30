using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Modal "Loyihalar" dialog opened from <see cref="AdminForm"/>'s main menu — lets the operator
/// create projects, delete them (which also deletes every presentation and file inside), and pick which one
/// is currently active. <see cref="SelectedActiveProjectId"/> reflects whatever the active project should be
/// once this dialog closes (by activation, or because the previously-active project was just deleted), and
/// is meaningful regardless of whether the dialog was closed via Yopish or by double-clicking a row — the
/// caller re-syncs against it either way.</summary>
public sealed class ProjectManagementForm : Form
{
    private readonly ProjectService _projectService;
    private readonly ListBox _listBox;
    private List<Project> _projects = [];

    /// <summary>The project that should be active once this dialog closes — starts as whatever was active
    /// when the dialog was opened, and only changes if the operator explicitly activates a different one or
    /// deletes the one that was active (in which case it becomes null).</summary>
    public int? SelectedActiveProjectId { get; private set; }

    public ProjectManagementForm(ProjectService projectService, int? currentActiveProjectId)
    {
        _projectService = projectService;
        SelectedActiveProjectId = currentActiveProjectId;

        Text = "Loyihalar";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 420);

        var header = new Label
        {
            Text = "2 marta bosing - faol loyiha sifatida tanlash",
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(16, 10, 16, 0),
            ForeColor = AppColors.TextSecondary,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic)
        };

        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(16),
            BackColor = AppColors.PanelAlt,
            ForeColor = AppColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 10.5f)
        };
        _listBox.DoubleClick += (_, _) => OnActivateClick(this, EventArgs.Empty);
        var listWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 4) };
        listWrap.Controls.Add(_listBox);

        var toolbarWrap = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(16, 0, 16, 8) };
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        var createButton = new Button { Text = "+ Yangi loyiha", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0), FlatStyle = FlatStyle.Flat, BackColor = AppColors.Success, ForeColor = AppColors.Background, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        createButton.Click += OnCreateClick;
        var deleteButton = new Button { Text = "O'chirish", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = AppColors.Danger, ForeColor = AppColors.TextPrimary, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        deleteButton.Click += OnDeleteClick;
        toolbar.Controls.Add(createButton, 0, 0);
        toolbar.Controls.Add(deleteButton, 1, 0);
        toolbarWrap.Controls.Add(toolbar);

        var activateButton = new Button { Text = "Faol qilish", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = AppColors.Accent, ForeColor = AppColors.Background, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        activateButton.Click += OnActivateClick;
        var activateWrap = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(16, 0, 16, 8) };
        activateWrap.Controls.Add(activateButton);

        var closeButton = new Button { Text = "Yopish", DialogResult = DialogResult.Cancel, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = AppColors.PanelAlt, ForeColor = AppColors.TextPrimary };
        var closeWrap = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(16, 0, 16, 12) };
        closeWrap.Controls.Add(closeButton);

        Controls.Add(listWrap);
        Controls.Add(activateWrap);
        Controls.Add(toolbarWrap);
        Controls.Add(header);
        Controls.Add(closeWrap);

        CancelButton = closeButton;

        Load += async (_, _) => await LoadProjectsAsync();
    }

    private async Task LoadProjectsAsync()
    {
        _projects = await _projectService.GetAllAsync();

        _listBox.BeginUpdate();
        _listBox.Items.Clear();
        foreach (var project in _projects)
        {
            var marker = project.Id == SelectedActiveProjectId ? "* " : "   ";
            _listBox.Items.Add($"{marker}{project.Name}");
        }
        _listBox.EndUpdate();
    }

    private Project? GetSelectedProject()
    {
        var index = _listBox.SelectedIndex;
        return index >= 0 && index < _projects.Count ? _projects[index] : null;
    }

    private async void OnCreateClick(object? sender, EventArgs e)
    {
        using var dialog = new ProjectEditForm();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _projectService.CreateAsync(
                dialog.ProjectName, dialog.EventStartDate, dialog.EventEndDate, dialog.EventTime, dialog.Location);
            await LoadProjectsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Loyiha yaratishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnDeleteClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedProject();
        if (selected is null)
        {
            return;
        }

        try
        {
            var presentationCount = await _projectService.CountPresentationsAsync(selected.Id);
            var warning = presentationCount > 0
                ? $"'{selected.Name}' loyihasi va undagi {presentationCount} ta taqdimot butunlay o'chirilsinmi?"
                : $"'{selected.Name}' loyihasi o'chirilsinmi?";

            var confirm = MessageBox.Show(this, warning, "O'chirishni tasdiqlash", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            await _projectService.DeleteAsync(selected.Id);
            if (SelectedActiveProjectId == selected.Id)
            {
                SelectedActiveProjectId = null;
            }

            await LoadProjectsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "O'chirishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnActivateClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedProject();
        if (selected is null)
        {
            return;
        }

        SelectedActiveProjectId = selected.Id;
        await LoadProjectsAsync();
        DialogResult = DialogResult.OK;
    }
}
