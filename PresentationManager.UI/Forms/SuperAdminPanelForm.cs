using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.UI.Localization;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>SuperAdmin's dashboard — read-only visibility over every table in the system (monitoring only,
/// no editing) with a single deliberate exception: creating new login accounts, since without that nobody
/// could ever populate the Users table the login system itself depends on.</summary>
public sealed class SuperAdminPanelForm : Form
{
    private static readonly string[] Sections =
    [
        "Loyihalar", "Taqdimotlar", "Taqdimotchilar", "Hakamlar", "Foydalanuvchilar", "Baholar", "Jurnal"
    ];

    private readonly ProjectService _projectService;
    private readonly PresentationQueueService _queueService;
    private readonly IPresenterRepository _presenterRepository;
    private readonly JudgeService _judgeService;
    private readonly UserService _userService;
    private readonly CriterionService _criterionService;
    private readonly ScoreService _scoreService;
    private readonly IHistoryRepository _historyRepository;

    private readonly ListBox _sectionList;
    private readonly DataGridView _grid;
    private readonly Button _addUserButton;

    public SuperAdminPanelForm(
        ProjectService projectService,
        PresentationQueueService queueService,
        IPresenterRepository presenterRepository,
        JudgeService judgeService,
        UserService userService,
        CriterionService criterionService,
        ScoreService scoreService,
        IHistoryRepository historyRepository)
    {
        _projectService = projectService;
        _queueService = queueService;
        _presenterRepository = presenterRepository;
        _judgeService = judgeService;
        _userService = userService;
        _criterionService = criterionService;
        _scoreService = scoreService;
        _historyRepository = historyRepository;

        Text = "SuperAdmin paneli";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 650);
        WindowState = FormWindowState.Maximized;

        _sectionList = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.Panel,
            ForeColor = AppColors.TextPrimary,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10.5f),
            IntegralHeight = false
        };
        _sectionList.Items.AddRange(Sections);
        _sectionList.SelectedIndexChanged += async (_, _) => await RefreshSelectedSectionAsync();

        var sectionWrap = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = AppColors.Panel, Padding = new Padding(8) };
        sectionWrap.Controls.Add(_sectionList);

        _grid = DataGridViewTheme.CreateReadOnlyGrid();
        var gridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        gridWrap.Controls.Add(_grid);

        _addUserButton = new Button
        {
            Text = "+ Foydalanuvchi qo'shish",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppColors.Success,
            ForeColor = AppColors.Background,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Visible = false
        };
        _addUserButton.Click += OnAddUserClick;
        var addUserWrap = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(12, 0, 12, 12) };
        addUserWrap.Controls.Add(_addUserButton);

        var contentPanel = new Panel { Dock = DockStyle.Fill };
        contentPanel.Controls.Add(gridWrap);
        contentPanel.Controls.Add(addUserWrap);

        Controls.Add(contentPanel);
        Controls.Add(sectionWrap);

        Load += (_, _) => _sectionList.SelectedIndex = 0;
    }

    private async Task RefreshSelectedSectionAsync()
    {
        var section = _sectionList.SelectedItem as string;
        _addUserButton.Visible = section == "Foydalanuvchilar";

        try
        {
            switch (section)
            {
                case "Loyihalar":
                    await LoadProjectsAsync();
                    break;
                case "Taqdimotlar":
                    await LoadPresentationsAsync();
                    break;
                case "Taqdimotchilar":
                    await LoadPresentersAsync();
                    break;
                case "Hakamlar":
                    await LoadJudgesAsync();
                    break;
                case "Foydalanuvchilar":
                    await LoadUsersAsync();
                    break;
                case "Baholar":
                    await LoadScoresAsync();
                    break;
                case "Jurnal":
                    await LoadHistoryAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ma'lumotlarni yuklashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LoadProjectsAsync()
    {
        var projects = await _projectService.GetAllAsync();
        _grid.DataSource = projects
            .Select(p => new
            {
                Nomi = p.Name,
                Boshlanish = p.EventStartDate.ToString("dd.MM.yyyy"),
                Tugash = p.EventEndDate.ToString("dd.MM.yyyy"),
                Vaqti = p.EventTime?.ToString("HH:mm") ?? "-",
                Manzil = p.Location ?? "-"
            })
            .ToList();
    }

    private async Task LoadPresentationsAsync()
    {
        var presentations = await _queueService.GetAllAsync();
        var projects = (await _projectService.GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);

        _grid.DataSource = presentations
            .Select(p => new
            {
                Loyiha = projects.GetValueOrDefault(p.ProjectId, "?"),
                Taqdimotchi = p.FullName,
                Sarlavha = p.Title,
                Holat = UzbekText.StatusLabel(p.Status),
                QoshilganVaqt = p.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            })
            .ToList();
    }

    private async Task LoadPresentersAsync()
    {
        var presenters = await _presenterRepository.GetAllAsync();
        _grid.DataSource = presenters
            .Select(p => new
            {
                Ism = p.FullName,
                Telefon = p.PhoneNumber ?? "-",
                Username = p.TelegramUsername is null ? "-" : $"@{p.TelegramUsername}",
                RoyxatdanOtganVaqt = p.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            })
            .ToList();
    }

    private async Task LoadJudgesAsync()
    {
        var judges = await _judgeService.GetAllAsync();
        var projects = (await _projectService.GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);

        _grid.DataSource = judges
            .Select(j => new
            {
                Loyiha = projects.GetValueOrDefault(j.ProjectId, "?"),
                Telefon = j.PhoneNumber,
                Ism = j.FullName ?? "-",
                Holat = j.TelegramChatId is not null ? "Bog'langan" : "Bog'lanmagan"
            })
            .ToList();
    }

    private async Task LoadUsersAsync()
    {
        var users = await _userService.GetAllAsync();
        _grid.DataSource = users
            .Select(u => new
            {
                Login = u.Username,
                Ism = u.FullName,
                Rol = u.Role.ToString(),
                Faol = u.IsActive ? "Ha" : "Yo'q",
                YaratilganVaqt = u.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            })
            .ToList();
    }

    private async Task LoadScoresAsync()
    {
        var scores = await _scoreService.GetAllAsync();
        var presentations = (await _queueService.GetAllAsync()).ToDictionary(p => p.Id);
        var judges = (await _judgeService.GetAllAsync()).ToDictionary(j => j.Id);
        var criteria = (await _criterionService.GetAllAsync()).ToDictionary(c => c.Id);

        _grid.DataSource = scores
            .Select(s => new
            {
                Taqdimot = presentations.TryGetValue(s.PresentationId, out var p) ? p.Title : "?",
                Hakam = judges.TryGetValue(s.JudgeId, out var j) ? j.PhoneNumber : "?",
                Mezon = criteria.TryGetValue(s.CriterionId, out var c) ? c.Name : "?",
                Ball = s.Value,
                YangilanganVaqt = s.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            })
            .ToList();
    }

    private async Task LoadHistoryAsync()
    {
        var history = await _historyRepository.GetRecentAsync(200);
        _grid.DataSource = history
            .Select(h => new { Vaqt = h.Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), Xabar = h.Message })
            .ToList();
    }

    private async void OnAddUserClick(object? sender, EventArgs e)
    {
        using var dialog = new AddUserForm();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _userService.CreateAsync(dialog.Username, dialog.Password, dialog.FullName, dialog.Role);
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Foydalanuvchi qo'shishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
