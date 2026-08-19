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
    private readonly TextBox _searchBox;

    /// <summary>Mirrors <see cref="_listBox"/>'s items in the same order - what a selected row resolves back
    /// to. <see cref="_allProjects"/> is the unfiltered source <see cref="ApplyFilter"/> re-applies on every
    /// <see cref="_searchBox"/> keystroke without re-fetching.</summary>
    private List<Project> _projects = [];
    private List<Project> _allProjects = [];

    /// <summary>The project that should be active once this dialog closes — starts as whatever was active
    /// when the dialog was opened, and only changes if the operator explicitly activates a different one or
    /// deletes the one that was active (in which case it becomes null).</summary>
    public int? SelectedActiveProjectId { get; private set; }

    public ProjectManagementForm(ProjectService projectService, int? currentActiveProjectId)
    {
        _projectService = projectService;
        SelectedActiveProjectId = currentActiveProjectId;

        Text = "Loyihalar";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 460);

        var header = new Label
        {
            Text = "2 marta bosing - faol loyiha sifatida tanlash",
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
        _listBox.DoubleClick += (_, _) => OnActivateClick(this, EventArgs.Empty);
        var listWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 8) };
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
        var toolbarWrap = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(16, 4, 16, 0) };
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        // Pinned to Percent(100), not left as the implicit AutoSize default: an AutoSize row lets the
        // TableLayoutPanel grow it to whatever height a Dock=Fill Button's GetPreferredSize reports once the
        // column is too narrow for its text on one line (word-wrap consideration) - the row then balloons
        // past toolbarWrap's fixed height and the button's vertically-centered text renders below the
        // visible, clipped area (paints, just invisible). Percent(100) keeps the row pinned to the
        // container's real height regardless of what children would prefer.
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        var searchWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0) };
        searchWrap.Controls.Add(_searchBox);
        var createButton = new Button
        {
            Text = "+ Yangi loyiha", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0), FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.Success, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        createButton.FlatAppearance.BorderSize = 0;
        createButton.Click += OnCreateClick;
        var deleteButton = new Button
        {
            Text = "O'chirish", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0), FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.Danger, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        deleteButton.FlatAppearance.BorderSize = 0;
        deleteButton.Click += OnDeleteClick;
        toolbar.Controls.Add(searchWrap, 0, 0);
        toolbar.Controls.Add(createButton, 1, 0);
        toolbar.Controls.Add(deleteButton, 2, 0);
        toolbarWrap.Controls.Add(toolbar);

        var activateButton = new Button
        {
            Text = "Faol qilish", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.Accent, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        activateButton.FlatAppearance.BorderSize = 0;
        activateButton.Click += OnActivateClick;
        var activateWrap = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(16, 0, 16, 8) };
        activateWrap.Controls.Add(activateButton);

        var closeButton = new Button
        {
            Text = "Yopish", DialogResult = DialogResult.Cancel, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, Cursor = Cursors.Hand
        };
        closeButton.FlatAppearance.BorderColor = LightColors.Border;
        var closeWrap = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(16, 0, 16, 12) };
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
        _allProjects = await _projectService.GetAllAsync();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = _searchBox.Text.Trim();
        _projects = string.IsNullOrEmpty(filter)
            ? _allProjects
            : _allProjects.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

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
