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
    private readonly PresentationBotOptions _botOptions;

    private readonly ComboBox _projectCombo;
    private readonly DataGridView _participantsGrid;
    private readonly DataGridView _presentationsGrid;
    private readonly DataGridView _finalScoresGrid;
    private List<Project> _projects = [];

    /// <summary>Set via <see cref="SetCurrentUser"/> right after this singleton form is resolved from DI in
    /// Program.cs (it's constructed without any user context, since login happens after the DI container is
    /// built) - scopes <see cref="LoadProjectsAsync"/> and <see cref="OnNewProjectClick"/> to this Admin's own
    /// projects, per <see cref="Project.CreatedByUserId"/>.</summary>
    private int? _currentUserId;

    private Project? SelectedProject => _projectCombo.SelectedItem as Project;

    public AdminPanelForm(
        ProjectService projectService,
        CriterionService criterionService,
        JudgeService judgeService,
        ScoreService scoreService,
        PresentationQueueService queueService,
        IPresenterRepository presenterRepository,
        AdminLinkService adminLinkService,
        IOptions<PresentationBotOptions> botOptions)
    {
        _projectService = projectService;
        _criterionService = criterionService;
        _judgeService = judgeService;
        _scoreService = scoreService;
        _queueService = queueService;
        _presenterRepository = presenterRepository;
        _adminLinkService = adminLinkService;
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

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f) };

        _participantsGrid = LightGrid();
        var participantsTab = new TabPage("Qatnashchilar") { BackColor = LightColors.Background };
        participantsTab.Controls.Add(_participantsGrid);

        _presentationsGrid = LightGrid();
        var presentationsTab = new TabPage("Taqdimotlar") { BackColor = LightColors.Background };
        presentationsTab.Controls.Add(_presentationsGrid);

        _finalScoresGrid = LightGrid();
        var finalScoresTab = new TabPage("Yakuniy baholar") { BackColor = LightColors.Background };
        finalScoresTab.Controls.Add(_finalScoresGrid);

        tabs.TabPages.Add(participantsTab);
        tabs.TabPages.Add(presentationsTab);
        tabs.TabPages.Add(finalScoresTab);

        Controls.Add(tabs);
        Controls.Add(topPanelRule);
        Controls.Add(topPanel);

        Load += async (_, _) => await LoadProjectsAsync();
    }

    /// <summary>Called once, from Program.cs, right after this form is resolved from DI and before it's run -
    /// see <see cref="_currentUserId"/>.</summary>
    public void SetCurrentUser(User user) => _currentUserId = user.Id;

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
        _presentationsGrid.DataSource = presentations
            .Select(p => new
            {
                Taqdimotchi = p.FullName,
                Sarlavha = p.Title,
                Holat = UzbekText.StatusLabel(p.Status),
                QoshilganVaqt = p.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            })
            .ToList();
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
    private void OnLinkBotClick(object? sender, EventArgs e) =>
        BotLinkHelper.ShowLinkDialog(this, _adminLinkService, _botOptions, _currentUserId);
}
