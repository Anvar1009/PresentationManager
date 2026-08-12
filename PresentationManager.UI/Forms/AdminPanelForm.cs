using System.Diagnostics;
using Microsoft.Extensions.Options;
using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.TelegramBot;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Admin role's dashboard — distinct from the operator-facing <see cref="AdminForm"/> (that name
/// predates the Admin/SuperAdmin roles and refers to the operator, not this role; kept as-is to avoid a
/// large unrelated rename). An Admin creates projects, defines their judging criteria and judges, and
/// reviews participants/presentations/final scores — everything here is scoped to whichever project is
/// selected at the top.</summary>
public sealed class AdminPanelForm : Form
{
    private readonly ProjectService _projectService;
    private readonly CriterionService _criterionService;
    private readonly JudgeService _judgeService;
    private readonly ScoreService _scoreService;
    private readonly PresentationQueueService _queueService;
    private readonly IPresenterRepository _presenterRepository;
    private readonly AdminLinkService _adminLinkService;
    private readonly IFileStorageService _fileStorageService;
    private readonly PresentationBotOptions _botOptions;

    private readonly ComboBox _projectCombo;
    private readonly DataGridView _participantsGrid;
    private readonly DataGridView _presentationsGrid;
    private readonly DataGridView _finalScoresGrid;
    private List<Project> _projects = [];

    /// <summary>Mirrors <see cref="_presentationsGrid"/>'s bound rows in the same order, since the grid is
    /// bound to an anonymous-object projection (display-only) that loses <see cref="Presentation.FilePath"/> -
    /// this is what a selected row is resolved back to for <see cref="OnOpenPresentationFileClick"/>.</summary>
    private List<Presentation> _currentPresentations = [];

    /// <summary>Set via <see cref="SetCurrentUser"/> right after this singleton form is resolved from DI in
    /// Program.cs (it's constructed without any user context, since login happens after the DI container is
    /// built) - scopes <see cref="LoadProjectsAsync"/> and <see cref="OnNewProjectClick"/> to this Admin's own
    /// projects, per <see cref="Project.CreatedByUserId"/>.</summary>
    private int? _currentUserId;

    /// <summary>Profile-info/Chiqish popup shown by <see cref="_userMenuButton"/>, in its own strip along the
    /// bottom of the window - populated once the logged-in user is known, see <see cref="SetCurrentUser"/>.</summary>
    private readonly ContextMenuStrip _userMenu = new();
    private readonly Button _userMenuButton;

    private Project? SelectedProject => _projectCombo.SelectedItem as Project;

    public AdminPanelForm(
        ProjectService projectService,
        CriterionService criterionService,
        JudgeService judgeService,
        ScoreService scoreService,
        PresentationQueueService queueService,
        IPresenterRepository presenterRepository,
        AdminLinkService adminLinkService,
        IFileStorageService fileStorageService,
        IOptions<PresentationBotOptions> botOptions)
    {
        _projectService = projectService;
        _criterionService = criterionService;
        _judgeService = judgeService;
        _scoreService = scoreService;
        _queueService = queueService;
        _presenterRepository = presenterRepository;
        _adminLinkService = adminLinkService;
        _fileStorageService = fileStorageService;
        _botOptions = botOptions.Value;

        Text = "Admin paneli";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 10.5f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        WindowState = FormWindowState.Maximized;

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(20, 12, 20, 12), BackColor = LightColors.Panel };
        var topPanelRule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = LightColors.Border };
        var topLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1 };
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        var projectLabel = new Label { Text = "Loyiha:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = LightColors.TextSecondary, Font = new Font("Segoe UI", 10.5f) };
        _projectCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(Project.Name),
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            Font = new Font("Segoe UI", 11.5f)
        };
        _projectCombo.SelectedIndexChanged += async (_, _) => await RefreshAllAsync();

        var newProjectButton = new Button { Text = "+ Yangi loyiha", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 4, 0), FlatStyle = FlatStyle.Flat, BackColor = LightColors.Success, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        newProjectButton.Click += OnNewProjectClick;

        var criteriaButton = new Button { Text = "Baholash mezonlari", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 4, 0), FlatStyle = FlatStyle.Flat, BackColor = LightColors.Accent, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        criteriaButton.Click += OnCriteriaClick;

        var judgesButton = new Button { Text = "Hakamlar", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 4, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(124, 58, 237), ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        judgesButton.Click += OnJudgesClick;

        var linkBotButton = new Button { Text = "🤖 Botga ulash", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        linkBotButton.FlatAppearance.BorderColor = LightColors.Border;
        linkBotButton.Click += OnLinkBotClick;

        topLayout.Controls.Add(projectLabel, 0, 0);
        topLayout.Controls.Add(_projectCombo, 1, 0);
        topLayout.Controls.Add(newProjectButton, 2, 0);
        topLayout.Controls.Add(criteriaButton, 3, 0);
        topLayout.Controls.Add(judgesButton, 4, 0);
        topLayout.Controls.Add(linkBotButton, 5, 0);
        topPanel.Controls.Add(topLayout);

        // ---------- Bottom bar: account info / Chiqish ----------
        // This form has no left nav column the way SuperAdminPanelForm does, so "bottom" here means its own
        // full-width strip along the foot of the window instead - the button itself still only takes up the
        // left portion of it (Dock.Left, not Fill) so it doesn't visually try to fill unrelated width.
        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(20, 8, 20, 8), BackColor = LightColors.Panel };
        var bottomPanelRule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = LightColors.Border };
        bottomPanel.Controls.Add(bottomPanelRule);

        // Populated once the logged-in user is known - see SetCurrentUser.
        _userMenuButton = new Button
        {
            Text = "👤",
            Dock = DockStyle.Left,
            Width = 220,
            FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            AutoEllipsis = true
        };
        _userMenuButton.FlatAppearance.BorderColor = LightColors.Border;
        // AboveRight, not the default below-the-control placement: this bar sits flush with the bottom of a
        // maximized window, so a downward-opening popup would routinely be clipped by (or fall behind) the
        // taskbar.
        _userMenuButton.Click += (_, _) => _userMenu.Show(_userMenuButton, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
        bottomPanel.Controls.Add(_userMenuButton);

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f) };

        _participantsGrid = LightGrid();
        var participantsTab = new TabPage("Qatnashchilar") { BackColor = LightColors.Background };
        participantsTab.Controls.Add(_participantsGrid);

        _presentationsGrid = LightGrid();
        _presentationsGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OnOpenPresentationFileClick(null, EventArgs.Empty); };
        var presentationsTab = new TabPage("Taqdimotlar") { BackColor = LightColors.Background };
        var presentationsToolbar = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(12, 8, 12, 8) };
        var openFileButton = new Button
        {
            Text = "📂 Faylni ochish", Dock = DockStyle.Left, Width = 190, FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.Accent, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        openFileButton.FlatAppearance.BorderSize = 0;
        openFileButton.Click += OnOpenPresentationFileClick;
        presentationsToolbar.Controls.Add(openFileButton);
        presentationsTab.Controls.Add(_presentationsGrid);
        presentationsTab.Controls.Add(presentationsToolbar);

        _finalScoresGrid = LightGrid();
        var finalScoresTab = new TabPage("Yakuniy baholar") { BackColor = LightColors.Background };
        var exportToolbar = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(12, 8, 12, 8) };
        var exportButton = new Button
        {
            Text = "📊 Excel'ga eksport", Dock = DockStyle.Left, Width = 190, FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.Success, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        exportButton.FlatAppearance.BorderSize = 0;
        exportButton.Click += OnExportFinalScoresClick;
        exportToolbar.Controls.Add(exportButton);
        finalScoresTab.Controls.Add(_finalScoresGrid);
        finalScoresTab.Controls.Add(exportToolbar);

        tabs.TabPages.Add(participantsTab);
        tabs.TabPages.Add(presentationsTab);
        tabs.TabPages.Add(finalScoresTab);

        Controls.Add(tabs);
        Controls.Add(bottomPanel);
        Controls.Add(topPanelRule);
        Controls.Add(topPanel);

        Load += async (_, _) => await LoadProjectsAsync();
    }

    /// <summary>Called once, from Program.cs, right after this form is resolved from DI and before it's run -
    /// see <see cref="_currentUserId"/>.</summary>
    public void SetCurrentUser(User user)
    {
        _currentUserId = user.Id;
        _userMenuButton.Text = $"👤 {user.FullName}";
        _userMenu.Items.Clear();
        _userMenu.Items.AddRange(UserMenuHelper.BuildItems(user, this));
    }

    private static DataGridView LightGrid() => DataGridViewTheme.CreateReadOnlyGrid(
        background: LightColors.Panel,
        alternatingBackground: LightColors.PanelAlt,
        headerBackground: LightColors.PanelAlt,
        headerForeground: LightColors.TextSecondary,
        textColor: LightColors.TextPrimary,
        accent: LightColors.Accent,
        selectionForeground: Color.White,
        gridLines: LightColors.Border);

    private async Task LoadProjectsAsync()
    {
        var selectedId = SelectedProject?.Id;
        _projects = _currentUserId is int userId
            ? await _projectService.GetByCreatorAsync(userId)
            : await _projectService.GetAllAsync();

        _projectCombo.DataSource = null;
        _projectCombo.DataSource = _projects;

        if (selectedId is int id)
        {
            var restored = _projects.FirstOrDefault(p => p.Id == id);
            if (restored is not null)
            {
                _projectCombo.SelectedItem = restored;
            }
        }
    }

    private async Task RefreshAllAsync()
    {
        var project = SelectedProject;
        if (project is null)
        {
            _participantsGrid.DataSource = null;
            _presentationsGrid.DataSource = null;
            _finalScoresGrid.Rows.Clear();
            _finalScoresGrid.Columns.Clear();
            return;
        }

        await RefreshParticipantsAsync(project.Id);
        await RefreshPresentationsAsync(project.Id);
        await RefreshFinalScoresAsync(project.Id);
    }

    private async Task RefreshParticipantsAsync(int projectId)
    {
        var participants = await _projectService.GetParticipantsAsync(projectId);
        _participantsGrid.DataSource = participants
            .Select(p => new { Ism = p.FullName, Telefon = p.PhoneNumber ?? "-", Taqdimotlar = p.PresentationCount })
            .ToList();
    }

    private async Task RefreshPresentationsAsync(int projectId)
    {
        var presentations = await _queueService.GetAllAsync(projectId);
        _currentPresentations = presentations;
        _presentationsGrid.DataSource = presentations
            .Select(p => new
            {
                Taqdimotchi = p.FullName,
                Sarlavha = p.Title,
                Holat = UzbekText.StatusLabel(p.Status),
                QoshilganVaqt = p.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                Fayl = Path.GetFileName(p.FilePath)
            })
            .ToList();
    }

    /// <summary>Opens the selected presentation's uploaded file with the OS's default viewer - the same file
    /// the presenter submitted via the Telegram bot, resolved from managed storage exactly as the operator's
    /// live preview does (see <c>AdminForm.UpdatePreviewAsync</c>).</summary>
    private async void OnOpenPresentationFileClick(object? sender, EventArgs e)
    {
        var rowIndex = _presentationsGrid.CurrentCell?.RowIndex ?? -1;
        if (rowIndex < 0 || rowIndex >= _currentPresentations.Count)
        {
            MessageBox.Show(this, "Avval taqdimotni tanlang.", "Taqdimot tanlanmagan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var presentation = _currentPresentations[rowIndex];
        var absolutePath = await _fileStorageService.GetAbsolutePathAsync(presentation.FilePath);
        if (!File.Exists(absolutePath))
        {
            MessageBox.Show(this, "Fayl topilmadi.", "Xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(absolutePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Faylni ochishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RefreshFinalScoresAsync(int projectId)
    {
        var criteria = await _criterionService.GetByProjectIdAsync(projectId);
        var summaries = await _scoreService.GetFinalScoresAsync(projectId);

        _finalScoresGrid.DataSource = null;
        _finalScoresGrid.Columns.Clear();
        _finalScoresGrid.Rows.Clear();

        _finalScoresGrid.Columns.Add("Presenter", "Taqdimotchi");
        _finalScoresGrid.Columns.Add("Title", "Sarlavha");
        foreach (var criterion in criteria)
        {
            _finalScoresGrid.Columns.Add($"c{criterion.Id}", $"{criterion.Name} (max {criterion.MaxScore})");
        }
        _finalScoresGrid.Columns.Add("Total", "Jami");

        foreach (var summary in summaries)
        {
            var row = new List<object> { summary.PresenterFullName, summary.Title };
            row.AddRange(criteria.Select(c => summary.AverageByCriterionId.GetValueOrDefault(c.Id, 0).ToString("0.##")));
            row.Add(summary.Total.ToString("0.##"));
            _finalScoresGrid.Rows.Add(row.ToArray());
        }
    }

    private async void OnNewProjectClick(object? sender, EventArgs e)
    {
        using var dialog = new ProjectEditForm();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _projectService.CreateAsync(
                dialog.ProjectName, dialog.EventStartDate, dialog.EventEndDate, dialog.EventTime, dialog.Location,
                _currentUserId);
            await LoadProjectsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Loyiha yaratishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnCriteriaClick(object? sender, EventArgs e)
    {
        var project = SelectedProject;
        if (project is null)
        {
            MessageBox.Show(this, "Avval loyihani tanlang.", "Loyiha tanlanmagan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new CriteriaManagementForm(_criterionService, project.Id, project.Name);
        dialog.ShowDialog(this);
        await RefreshFinalScoresAsync(project.Id);
    }

    private async void OnJudgesClick(object? sender, EventArgs e)
    {
        var project = SelectedProject;
        if (project is null)
        {
            MessageBox.Show(this, "Avval loyihani tanlang.", "Loyiha tanlanmagan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new JudgeManagementForm(_judgeService, _presenterRepository, project.Id, project.Name);
        dialog.ShowDialog(this);
    }

    /// <summary>Generates a one-time, 15-minute deep-link code (<see cref="AdminLinkService"/>, via
    /// <see cref="BotLinkHelper"/>) that links this Admin's desktop account to whichever Telegram chat opens
    /// it - after that, the bot shows them the same projects/participants/presentations/final scores this
    /// panel does, plus basic project/criteria/judge management, and it's also what lets "Parolni
    /// unutdingizmi?" deliver reset codes to this account.</summary>
    private async void OnLinkBotClick(object? sender, EventArgs e) =>
        await BotLinkHelper.ShowLinkDialogAsync(this, _adminLinkService, _botOptions, _currentUserId);

    private void OnExportFinalScoresClick(object? sender, EventArgs e)
    {
        var project = SelectedProject;
        if (project is null)
        {
            MessageBox.Show(this, "Avval loyihani tanlang.", "Loyiha tanlanmagan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_finalScoresGrid.Rows.Count == 0)
        {
            MessageBox.Show(this, "Eksport qilish uchun ma'lumot yo'q.", "Bo'sh jadval", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var safeName = string.Join("_", project.Name.Split(Path.GetInvalidFileNameChars()));
        using var dialog = new SaveFileDialog
        {
            Filter = "Excel fayli (*.xlsx)|*.xlsx",
            FileName = $"{safeName} - Yakuniy baholar.xlsx"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ExcelExportHelper.ExportGrid(_finalScoresGrid, dialog.FileName, "Yakuniy baholar");
            MessageBox.Show(this, "Excel fayli muvaffaqiyatli saqlandi.", "Eksport", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Eksport qilishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
