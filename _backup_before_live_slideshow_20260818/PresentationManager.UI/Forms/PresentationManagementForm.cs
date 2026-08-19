using PresentationManager.Application.Common;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Modal "Taqdimotlar" dialog opened from <see cref="AdminForm"/>'s main menu — lets the operator
/// add/edit/delete presentations across every project, not just whichever one is currently active in the
/// live queue (presentations otherwise only ever enter the system via the Telegram bot). Read-only grid plus
/// Qo'shish/Tahrirlash/O'chirish, mirroring <see cref="ProjectManagementForm"/>'s layout.</summary>
public sealed class PresentationManagementForm : Form
{
    private readonly PresentationQueueService _queueService;
    private readonly ProjectService _projectService;

    private readonly DataGridView _grid;
    private readonly TextBox _searchBox;

    /// <summary>Mirrors <see cref="_grid"/>'s bound rows in the same order - what a selected row resolves
    /// back to. <see cref="_allPresentations"/> is the unfiltered source <see cref="ApplyFilter"/> re-applies
    /// on every keystroke without re-fetching.</summary>
    private List<Presentation> _presentations = [];
    private List<Presentation> _allPresentations = [];
    private List<Project> _projects = [];

    public PresentationManagementForm(PresentationQueueService queueService, ProjectService projectService)
    {
        _queueService = queueService;
        _projectService = projectService;

        Text = "Taqdimotlar";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(640, 420);

        _grid = DataGridViewTheme.CreateReadOnlyGrid(
            background: LightColors.Panel,
            alternatingBackground: LightColors.PanelAlt,
            headerBackground: LightColors.PanelAlt,
            headerForeground: LightColors.TextSecondary,
            textColor: LightColors.TextPrimary,
            accent: LightColors.Accent,
            selectionForeground: Color.White,
            gridLines: LightColors.Border);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OnEditClick(this, EventArgs.Empty); };
        var gridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 16, 8) };
        gridWrap.Controls.Add(_grid);

        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Loyiha, taqdimotchi yoki sarlavha bo'yicha qidirish...",
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        _searchBox.TextChanged += (_, _) => ApplyFilter();

        // One row shared with the action buttons below, not a full-width strip of its own: the search box
        // is fixed-width on the left, buttons split the remaining space on the right.
        var toolbarWrap = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(16, 8, 16, 0) };
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        // Pinned to Percent(100), not left as the implicit AutoSize default: an AutoSize row lets the
        // TableLayoutPanel grow it to whatever height a Dock=Fill Button's GetPreferredSize reports once the
        // column is too narrow for its text on one line (word-wrap consideration) - the row then balloons
        // past toolbarWrap's fixed height and the button's vertically-centered text renders below the
        // visible, clipped area (paints, just invisible). Percent(100) keeps the row pinned to the
        // container's real height regardless of what children would prefer.
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        var searchWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0) };
        searchWrap.Controls.Add(_searchBox);
        var addButton = new Button
        {
            Text = "+ Qo'shish", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0), FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.Success, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        addButton.FlatAppearance.BorderSize = 0;
        addButton.Click += OnAddClick;
        var editButton = new Button
        {
            Text = "✏️ Tahrirlash", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 6, 0), FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.Accent, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        editButton.FlatAppearance.BorderSize = 0;
        editButton.Click += OnEditClick;
        var deleteButton = new Button
        {
            Text = "🗑️ O'chirish", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0), FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.Danger, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        deleteButton.FlatAppearance.BorderSize = 0;
        deleteButton.Click += OnDeleteClick;
        toolbar.Controls.Add(searchWrap, 0, 0);
        toolbar.Controls.Add(addButton, 1, 0);
        toolbar.Controls.Add(editButton, 2, 0);
        toolbar.Controls.Add(deleteButton, 3, 0);
        toolbarWrap.Controls.Add(toolbar);

        var closeButton = new Button
        {
            Text = "Yopish", DialogResult = DialogResult.Cancel, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, Cursor = Cursors.Hand
        };
        closeButton.FlatAppearance.BorderColor = LightColors.Border;
        var closeWrap = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(16, 0, 16, 12) };
        closeWrap.Controls.Add(closeButton);

        Controls.Add(gridWrap);
        Controls.Add(toolbarWrap);
        Controls.Add(closeWrap);

        CancelButton = closeButton;

        Load += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _projects = await _projectService.GetAllAsync();
        _allPresentations = await _queueService.GetAllAsync();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var projectNames = _projects.ToDictionary(p => p.Id, p => p.Name);
        var filter = _searchBox.Text.Trim();
        _presentations = string.IsNullOrEmpty(filter)
            ? _allPresentations
            : _allPresentations.Where(p =>
                    p.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || p.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || projectNames.GetValueOrDefault(p.ProjectId, "?").Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        _grid.DataSource = _presentations
            .Select(p => new
            {
                Loyiha = projectNames.GetValueOrDefault(p.ProjectId, "?"),
                Taqdimotchi = p.FullName,
                Sarlavha = p.Title,
                Holat = UzbekText.StatusLabel(p.Status),
                QoshilganVaqt = p.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            })
            .ToList();
    }

    private Presentation? GetSelectedPresentation()
    {
        var index = _grid.CurrentCell?.RowIndex ?? -1;
        return index >= 0 && index < _presentations.Count ? _presentations[index] : null;
    }

    private async void OnAddClick(object? sender, EventArgs e)
    {
        if (_projects.Count == 0)
        {
            MessageBox.Show(this, "Avval kamida bitta loyiha yarating.", "Loyiha yo'q", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new PresentationEditForm("Yangi taqdimot", requireFile: true, _projects);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _queueService.AddAsync(
                dialog.ProjectId!.Value, dialog.FullName, dialog.Title, dialog.SelectedFilePath!, dialog.SelectedFileType!.Value,
                dialog.PresentationTimeSeconds, dialog.DiscussionTimeSeconds, dialog.ExtraDiscussionTimeSeconds);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Qo'shishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnEditClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedPresentation();
        if (selected is null)
        {
            return;
        }

        using var dialog = new PresentationEditForm("Taqdimotni tahrirlash", requireFile: false, _projects);
        dialog.LoadFrom(selected);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _queueService.UpdateAsync(
                selected.Id, dialog.FullName, dialog.Title, dialog.PresentationTimeSeconds, dialog.DiscussionTimeSeconds,
                dialog.ExtraDiscussionTimeSeconds, dialog.SelectedFilePath, dialog.SelectedFileType);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Tahrirlashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnDeleteClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedPresentation();
        if (selected is null)
        {
            MessageBox.Show(this, "Avval taqdimotni tanlang.", "Taqdimot tanlanmagan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this, $"'{selected.Title}' taqdimoti butunlay o'chirilsinmi?", "O'chirishni tasdiqlash",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _queueService.DeleteAsync(selected.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "O'chirishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
