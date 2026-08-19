using System.Diagnostics;
using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.UI.Controls;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>SuperAdmin's dashboard — read-only visibility over every table in the system (monitoring only,
/// no editing) with a single deliberate exception: creating new login accounts, since without that nobody
/// could ever populate the Users table the login system itself depends on.</summary>
public sealed class SuperAdminPanelForm : Form
{
    private static readonly (string Icon, string Label)[] Sections =
    [
        ("📁", "Loyihalar"),
        ("🎤", "Taqdimotlar"),
        ("🎓", "Taqdimotchilar"),
        ("⚖️", "Hakamlar"),
        ("👥", "Foydalanuvchilar"),
        ("⭐", "Baholar"),
        ("🕒", "Jurnal")
    ];

    private readonly ProjectService _projectService;
    private readonly PresentationQueueService _queueService;
    private readonly IPresenterRepository _presenterRepository;
    private readonly JudgeService _judgeService;
    private readonly UserService _userService;
    private readonly CriterionService _criterionService;
    private readonly ScoreService _scoreService;
    private readonly IHistoryRepository _historyRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly ITelegramSender _telegramSender;

    private readonly SectionNavListBox _sectionList;
    private readonly Label _sectionTitleLabel;
    private readonly Label _rowCountLabel;
    private readonly TextBox _searchBox;
    private readonly DataGridView _grid;
    private readonly RoundedButton _addUserButton;
    private readonly RoundedButton _editUserButton;
    private readonly RoundedButton _openFileButton;
    private readonly RoundedButton _clearHistoryButton;
    private readonly ModernOutlineButton _refreshButton;

    /// <summary>Profile-info/Chiqish popup shown by <see cref="_userMenuButton"/> - populated once the
    /// logged-in user is known, see <see cref="SetCurrentUser"/>. Unlike AdminForm/AdminPanelForm, this form
    /// previously had no <c>SetCurrentUser</c> at all - Program.cs is updated alongside this to call it.</summary>
    private readonly ContextMenuStrip _userMenu = new();
    private readonly Button _userMenuButton;

    /// <summary>Same instance handed to <see cref="SetCurrentUser"/> - kept around (and mutated in place by
    /// <see cref="UserMenuHelper"/> on a successful self-service login change) so <see cref="RefreshUserMenu"/>
    /// can rebuild the popup without re-fetching from the API.</summary>
    private User? _currentUser;

    /// <summary>Mirrors <see cref="_grid"/>'s bound rows, in the same order, whenever the "Taqdimotlar"
    /// section is loaded — the grid is bound to an anonymous-object projection (display-only) that loses
    /// <see cref="Presentation.FilePath"/>, so this is what a selected row is resolved back to for
    /// <see cref="OnOpenPresentationFileClick"/>. Left stale (not cleared) when another section is loaded,
    /// same as <c>AdminPanelForm._currentPresentations</c> — harmless since the button is hidden then.</summary>
    private List<Presentation> _currentPresentations = [];

    /// <summary>Same idea as <see cref="_currentPresentations"/>, for the "Foydalanuvchilar" section — lets a
    /// selected grid row be resolved back to its <see cref="User"/> for <see cref="OnEditUserClick"/>.</summary>
    private List<User> _currentUsers = [];

    // ---------- Unfiltered per-section data, cached so _searchBox's TextChanged can re-filter and rebind
    // the shared _grid on every keystroke without re-fetching from the API. Only ever one of these is
    // "current" at a time (whichever section is selected) - see ApplyCurrentSectionFilter. ----------
    private List<Project> _allProjects = [];
    private List<Presentation> _allPresentations = [];
    private Dictionary<int, string> _presentationProjectNames = [];
    private List<Presenter> _allPresenters = [];
    private List<Judge> _allJudges = [];
    private Dictionary<int, string> _judgeProjectNames = [];
    private List<User> _allUsers = [];
    private List<Score> _allScores = [];
    private Dictionary<int, Presentation> _scorePresentations = [];
    private Dictionary<int, Judge> _scoreJudges = [];
    private Dictionary<int, EvaluationCriterion> _scoreCriteria = [];
    private List<HistoryEntry> _allHistory = [];

    public SuperAdminPanelForm(
        ProjectService projectService,
        PresentationQueueService queueService,
        IPresenterRepository presenterRepository,
        JudgeService judgeService,
        UserService userService,
        CriterionService criterionService,
        ScoreService scoreService,
        IHistoryRepository historyRepository,
        IFileStorageService fileStorageService,
        ITelegramSender telegramSender)
    {
        _projectService = projectService;
        _queueService = queueService;
        _presenterRepository = presenterRepository;
        _judgeService = judgeService;
        _userService = userService;
        _criterionService = criterionService;
        _scoreService = scoreService;
        _historyRepository = historyRepository;
        _fileStorageService = fileStorageService;
        _telegramSender = telegramSender;

        Text = "SuperAdmin paneli";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1050, 680);
        WindowState = FormWindowState.Maximized;

        // ---------- Top header ----------
        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = LightColors.Panel, Padding = new Padding(28, 0, 24, 0) };
        var headerBottomRule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = LightColors.Border };
        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));

        var titleStack = new Panel { Dock = DockStyle.Fill };
        var titleLabel = new Label
        {
            Text = "SuperAdmin paneli",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = LightColors.TextPrimary,
            TextAlign = ContentAlignment.BottomLeft
        };
        var subtitleLabel = new Label
        {
            Text = "Barcha jadvallar va jarayonlar — faqat ko'rish rejimi",
            Dock = DockStyle.Top,
            Height = 20,
            Font = new Font("Segoe UI", 9f),
            ForeColor = LightColors.TextSecondary,
            TextAlign = ContentAlignment.TopLeft
        };
        titleStack.Controls.Add(subtitleLabel);
        titleStack.Controls.Add(titleLabel);

        // Dock.Fill inside a 165px column with a 20px right margin yields the exact 145px width; symmetric
        // top/bottom margin against the 72px header row controls the height (well under the 40px spec
        // default here) while keeping it vertically centered, never touching the row's edges. At this
        // height the control's own default vertical Padding (10/10) would leave only 4px for content and
        // clip the 16px icon, so it's tightened here to just fit it.
        _refreshButton = new ModernOutlineButton
        {
            Text = "Yangilash",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 24, 20, 24),
            Padding = new Padding(18, 4, 18, 4)
        };
        _refreshButton.Click += async (_, _) => await RefreshSelectedSectionAsync();

        headerLayout.Controls.Add(titleStack, 0, 0);
        headerLayout.Controls.Add(_refreshButton, 1, 0);
        header.Controls.Add(headerLayout);

        // ---------- Left nav ----------
        _sectionList = new SectionNavListBox();
        _sectionList.SetSections(Sections);
        _sectionList.SelectedIndexChanged += async (_, _) =>
        {
            // A search relevant to the previous section rarely means anything for the new one - starting
            // fresh avoids landing on an unrelated section that looks unexpectedly empty.
            _searchBox.Text = string.Empty;
            await RefreshSelectedSectionAsync();
        };

        // Populated once the logged-in user is known - see SetCurrentUser. Sits below the section list
        // (Loyihalar/.../Jurnal) rather than up in the header, so account info/Chiqish reads as belonging to
        // this nav column instead of floating next to Yangilash.
        _userMenuButton = new Button
        {
            Text = "👤",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            AutoEllipsis = true
        };
        _userMenuButton.FlatAppearance.BorderColor = LightColors.Border;
        // AboveRight, not the default below-the-control placement: this button sits at the very bottom of a
        // maximized window, so a downward-opening popup would routinely be clipped by (or fall behind) the
        // taskbar.
        _userMenuButton.Click += (_, _) => _userMenu.Show(_userMenuButton, new Point(0, 0), ToolStripDropDownDirection.AboveRight);

        var userMenuWrap = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 12, 0, 0) };
        userMenuWrap.Controls.Add(_userMenuButton);

        var navWrap = new Panel { Dock = DockStyle.Left, Width = 232, BackColor = LightColors.Panel, Padding = new Padding(10, 16, 10, 16) };
        navWrap.Controls.Add(_sectionList);
        navWrap.Controls.Add(userMenuWrap);

        var navDivider = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = LightColors.Border };

        // ---------- Content: card header (section title + count + add-user) over the grid ----------
        var contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Background, Padding = new Padding(24) };

        var card = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Panel };
        card.Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(LightColors.Border), 0, 0, card.Width - 1, card.Height - 1);

        var cardHeader = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(20, 0, 16, 0) };
        var cardHeaderLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        // Fixed, not AutoSize: _sectionTitleLabel below is Dock=Fill (needed to vertically center its text
        // in the row via TextAlign), and an AutoSize column paired with a Dock=Fill child is a circular
        // layout dependency WinForms doesn't resolve reliably - in practice the column could end up sized
        // for whatever text was set at the very first (pre-Maximize) layout pass and not grow for a longer
        // label typed in later, clipping it down to just the icon (seen with "👥 Foydalanuvchilar"/
        // "📁 Loyihalar" after switching sections). Wide enough for the longest label, "Foydalanuvchilar".
        cardHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        cardHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Wide enough for the search box, shrunk down from a full-width row of its own so it shares this
        // header row with the section-action buttons instead of taking a separate strip below.
        cardHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        // Wide enough to fit two of the section-action buttons side by side (Foydalanuvchilar shows both
        // _addUserButton and _editUserButton at once) - see the FlowLayoutPanel below.
        cardHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));

        // TopLeft + an explicit top Padding, not MiddleLeft: vertically centering this text depends on
        // accurately measuring its own height, and the color emoji glyph mixed into the same string/font as
        // the bold Latin title text throws that measurement off (its em-box commonly sits lower/taller than
        // the surrounding Latin text's) - the label kept ending up low enough to crowd into cardHeaderRule
        // right below it (looked "stuck to" the grid's own header row). Pinning to a fixed offset from the
        // top instead sidesteps that measurement entirely, independent of exactly how tall the row ends up.
        _sectionTitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(0, 6, 0, 0),
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = LightColors.TextPrimary
        };
        _rowCountLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(12, 10, 0, 0),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = LightColors.TextSecondary
        };

        _addUserButton = new RoundedButton
        {
            Text = "+ Foydalanuvchi qo'shish",
            Margin = new Padding(6, 8, 0, 8),
            BackColor = LightColors.Success,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            CornerRadius = 8,
            Visible = false
        };
        _addUserButton.AutoFitToText();
        _addUserButton.Click += OnAddUserClick;

        _editUserButton = new RoundedButton
        {
            Text = "✏️ Login/parolni tiklash",
            Margin = new Padding(6, 8, 0, 8),
            BackColor = LightColors.Accent,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            CornerRadius = 8,
            Visible = false
        };
        _editUserButton.AutoFitToText();
        _editUserButton.Click += OnEditUserClick;

        _openFileButton = new RoundedButton
        {
            Text = "📂 Faylni ochish",
            Margin = new Padding(6, 8, 0, 8),
            BackColor = LightColors.Accent,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            CornerRadius = 8,
            Visible = false
        };
        _openFileButton.AutoFitToText();
        _openFileButton.Click += OnOpenPresentationFileClick;

        _clearHistoryButton = new RoundedButton
        {
            Text = "🗑️ Jurnalni tozalash",
            Margin = new Padding(6, 8, 0, 8),
            BackColor = LightColors.Danger,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            CornerRadius = 8,
            Visible = false
        };
        _clearHistoryButton.AutoFitToText();
        _clearHistoryButton.Click += OnClearHistoryClick;

        // RightToLeft flow so _addUserButton (added first) lands at the same far-right spot it always has;
        // _editUserButton then takes the slot immediately to its left when Foydalanuvchilar shows both at
        // once. _openFileButton/_clearHistoryButton never appear alongside either (different sections), so
        // their position among them doesn't matter.
        var sectionActionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = false
        };
        sectionActionsPanel.Controls.Add(_addUserButton);
        sectionActionsPanel.Controls.Add(_editUserButton);
        sectionActionsPanel.Controls.Add(_openFileButton);
        sectionActionsPanel.Controls.Add(_clearHistoryButton);

        // Shares the header row with sectionActionsPanel (column 3) rather than taking a full-width strip
        // of its own below the header.
        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Qidirish...",
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        _searchBox.TextChanged += (_, _) => ApplyCurrentSectionFilter();
        var searchBoxWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 14, 12, 14) };
        searchBoxWrap.Controls.Add(_searchBox);

        cardHeaderLayout.Controls.Add(_sectionTitleLabel, 0, 0);
        cardHeaderLayout.Controls.Add(_rowCountLabel, 1, 0);
        cardHeaderLayout.Controls.Add(searchBoxWrap, 2, 0);
        cardHeaderLayout.Controls.Add(sectionActionsPanel, 3, 0);
        cardHeader.Controls.Add(cardHeaderLayout);

        var cardHeaderRule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = LightColors.Border };

        _grid = DataGridViewTheme.CreateReadOnlyGrid(
            background: LightColors.Panel,
            alternatingBackground: LightColors.PanelAlt,
            headerBackground: LightColors.PanelAlt,
            headerForeground: LightColors.TextSecondary,
            textColor: LightColors.TextPrimary,
            accent: LightColors.Accent,
            selectionForeground: Color.White,
            gridLines: LightColors.Border);
        _grid.CellDoubleClick += OnGridCellDoubleClick;
        var gridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        gridWrap.Controls.Add(_grid);

        card.Controls.Add(gridWrap);
        card.Controls.Add(cardHeaderRule);
        card.Controls.Add(cardHeader);
        contentPanel.Controls.Add(card);

        Controls.Add(contentPanel);
        Controls.Add(navDivider);
        Controls.Add(navWrap);
        Controls.Add(headerBottomRule);
        Controls.Add(header);

        Load += (_, _) => _sectionList.SelectedIndex = 0;
    }

    /// <summary>Called once, from Program.cs, right after this form is resolved from DI and before it's run -
    /// mirrors AdminForm/AdminPanelForm's own <c>SetCurrentUser</c>, which this form never had before since
    /// nothing here previously needed to know who was logged in.</summary>
    public void SetCurrentUser(User user)
    {
        _currentUser = user;
        _userMenuButton.Text = $"👤 {user.Role}";
        RefreshUserMenu();
    }

    /// <summary>Rebuilds the account popup from <see cref="_currentUser"/> - called once from
    /// <see cref="SetCurrentUser"/> and again after a successful self-service login change (see
    /// <see cref="UserMenuHelper"/>) so the bold "Username · Role" row reflects the new login.</summary>
    private void RefreshUserMenu()
    {
        _userMenu.Items.Clear();
        _userMenu.Items.AddRange(UserMenuHelper.BuildItems(_currentUser!, _userService, this, RefreshUserMenu));
    }

    private async Task RefreshSelectedSectionAsync()
    {
        if (_sectionList.SelectedItem is not (string icon, string label))
        {
            return;
        }

        _sectionTitleLabel.Text = $"{icon} {label}";
        _addUserButton.Visible = label == "Foydalanuvchilar";
        _editUserButton.Visible = label == "Foydalanuvchilar";
        _openFileButton.Visible = label == "Taqdimotlar";
        _clearHistoryButton.Visible = label == "Jurnal";

        try
        {
            switch (label)
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

            _rowCountLabel.Text = $"{_grid.RowCount} ta yozuv";
        }
        catch (Exception ex)
        {
            _rowCountLabel.Text = string.Empty;
            MessageBox.Show(this, ex.Message, "Ma'lumotlarni yuklashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Re-filters and rebinds <see cref="_grid"/> from whichever section's already-loaded data is
    /// current, without re-fetching - called on every <see cref="_searchBox"/> keystroke. Each Load*Async
    /// below calls its own Apply*Filter counterpart once after fetching, so this only needs to route to the
    /// right one by section label.</summary>
    private void ApplyCurrentSectionFilter()
    {
        if (_sectionList.SelectedItem is not (string, string label))
        {
            return;
        }

        switch (label)
        {
            case "Loyihalar": ApplyProjectsFilter(); break;
            case "Taqdimotlar": ApplyPresentationsFilter(); break;
            case "Taqdimotchilar": ApplyPresentersFilter(); break;
            case "Hakamlar": ApplyJudgesFilter(); break;
            case "Foydalanuvchilar": ApplyUsersFilter(); break;
            case "Baholar": ApplyScoresFilter(); break;
            case "Jurnal": ApplyHistoryFilter(); break;
        }

        _rowCountLabel.Text = $"{_grid.RowCount} ta yozuv";
    }

    private async Task LoadProjectsAsync()
    {
        _allProjects = await _projectService.GetAllAsync();
        ApplyProjectsFilter();
    }

    private void ApplyProjectsFilter()
    {
        var filter = _searchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allProjects
            : _allProjects.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        _grid.DataSource = filtered
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
        _allPresentations = await _queueService.GetAllAsync();
        _presentationProjectNames = (await _projectService.GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);
        ApplyPresentationsFilter();
    }

    private void ApplyPresentationsFilter()
    {
        var filter = _searchBox.Text.Trim();
        _currentPresentations = string.IsNullOrEmpty(filter)
            ? _allPresentations
            : _allPresentations.Where(p =>
                    p.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || p.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || _presentationProjectNames.GetValueOrDefault(p.ProjectId, "?").Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        _grid.DataSource = _currentPresentations
            .Select(p => new
            {
                Loyiha = _presentationProjectNames.GetValueOrDefault(p.ProjectId, "?"),
                Taqdimotchi = p.FullName,
                Sarlavha = p.Title,
                Holat = UzbekText.StatusLabel(p.Status),
                QoshilganVaqt = p.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                Fayl = Path.GetFileName(p.FilePath)
            })
            .ToList();
    }

    /// <summary>Routes a grid row double-click to whichever action the currently selected section actually
    /// supports — the same shared <see cref="_grid"/> is reused across all sections, so only one of these two
    /// ever applies at a time (mirrored by which action button is <see cref="Control.Visible"/>).</summary>
    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        if (_openFileButton.Visible)
        {
            OnOpenPresentationFileClick(null, EventArgs.Empty);
        }
        else if (_editUserButton.Visible)
        {
            OnEditUserClick(null, EventArgs.Empty);
        }
    }

    /// <summary>Opens the selected presentation's uploaded file with the OS's default viewer — same mechanism
    /// as <c>AdminPanelForm.OnOpenPresentationFileClick</c>.</summary>
    private async void OnOpenPresentationFileClick(object? sender, EventArgs e)
    {
        if (!_openFileButton.Visible)
        {
            return;
        }

        var rowIndex = _grid.CurrentCell?.RowIndex ?? -1;
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

    private async Task LoadPresentersAsync()
    {
        _allPresenters = await _presenterRepository.GetAllAsync();
        ApplyPresentersFilter();
    }

    private void ApplyPresentersFilter()
    {
        var filter = _searchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allPresenters
            : _allPresenters.Where(p =>
                    p.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (p.PhoneNumber?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (p.TelegramUsername?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

        _grid.DataSource = filtered
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
        _allJudges = await _judgeService.GetAllAsync();
        _judgeProjectNames = (await _projectService.GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);
        ApplyJudgesFilter();
    }

    private void ApplyJudgesFilter()
    {
        var filter = _searchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allJudges
            : _allJudges.Where(j =>
                    (j.FullName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                    || j.PhoneNumber.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || _judgeProjectNames.GetValueOrDefault(j.ProjectId, "?").Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        _grid.DataSource = filtered
            .Select(j => new
            {
                Loyiha = _judgeProjectNames.GetValueOrDefault(j.ProjectId, "?"),
                Telefon = j.PhoneNumber,
                Ism = j.FullName ?? "-",
                Holat = j.TelegramChatId is not null ? "Bog'langan" : "Bog'lanmagan"
            })
            .ToList();
    }

    private async Task LoadUsersAsync()
    {
        _allUsers = await _userService.GetAllAsync();
        ApplyUsersFilter();
    }

    private void ApplyUsersFilter()
    {
        var filter = _searchBox.Text.Trim();
        _currentUsers = string.IsNullOrEmpty(filter)
            ? _allUsers
            : _allUsers.Where(u =>
                    u.Username.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || u.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        _grid.DataSource = _currentUsers
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
        _allScores = await _scoreService.GetAllAsync();
        _scorePresentations = (await _queueService.GetAllAsync()).ToDictionary(p => p.Id);
        _scoreJudges = (await _judgeService.GetAllAsync()).ToDictionary(j => j.Id);
        _scoreCriteria = (await _criterionService.GetAllAsync()).ToDictionary(c => c.Id);
        ApplyScoresFilter();
    }

    private void ApplyScoresFilter()
    {
        var filter = _searchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allScores
            : _allScores.Where(s =>
                    (_scorePresentations.TryGetValue(s.PresentationId, out var p) && p.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    || (_scoreJudges.TryGetValue(s.JudgeId, out var j) && j.PhoneNumber.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    || (_scoreCriteria.TryGetValue(s.CriterionId, out var c) && c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        _grid.DataSource = filtered
            .Select(s => new
            {
                Taqdimot = _scorePresentations.TryGetValue(s.PresentationId, out var p) ? p.Title : "?",
                Hakam = _scoreJudges.TryGetValue(s.JudgeId, out var j) ? j.PhoneNumber : "?",
                Mezon = _scoreCriteria.TryGetValue(s.CriterionId, out var c) ? c.Name : "?",
                Ball = s.Value,
                YangilanganVaqt = s.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            })
            .ToList();
    }

    private async Task LoadHistoryAsync()
    {
        _allHistory = await _historyRepository.GetRecentAsync(200);
        ApplyHistoryFilter();
    }

    private void ApplyHistoryFilter()
    {
        var filter = _searchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allHistory
            : _allHistory.Where(h => h.Message.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        _grid.DataSource = filtered
            .Select(h => new { Vaqt = h.Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), Xabar = h.Message })
            .ToList();
    }

    /// <summary>Promotes someone who already registered through the Telegram bot (a <see cref="Presenter"/>)
    /// to a desktop login account — nothing is typed by hand, only picked: which presenter, and which role.
    /// The generated login/password (see <see cref="UserService.CreateFromPresenterAsync"/>) are delivered to
    /// that person over the same Telegram chat, not shown for the SuperAdmin to relay manually unless that
    /// delivery fails.</summary>
    private async void OnAddUserClick(object? sender, EventArgs e)
    {
        var presenters = await _presenterRepository.GetAllAsync();
        var users = await _userService.GetAllAsync();
        var linkedChatIds = users.Where(u => u.TelegramChatId.HasValue).Select(u => u.TelegramChatId!.Value).ToHashSet();
        var availablePresenters = presenters.Where(p => !linkedChatIds.Contains(p.TelegramChatId)).ToList();

        if (availablePresenters.Count == 0)
        {
            MessageBox.Show(
                this,
                "Telegram bot orqali ro'yxatdan o'tgan, hali foydalanuvchi qilib belgilanmagan taqdimotchi yo'q.",
                "Foydalanuvchi qo'shish",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new AddUserForm(availablePresenters);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var (user, generatedPassword) = await _userService.CreateFromPresenterAsync(dialog.SelectedPresenter, dialog.Role);

            var sent = user.TelegramChatId is { } chatId && await _telegramSender.TrySendMessageAsync(
                chatId,
                $"✅ Sizga \"{user.Role}\" huquqi berildi!\n\nDastur uchun kirish ma'lumotlaringiz:\nLogin: {user.Username}\nParol: {generatedPassword}\n\nXavfsizlik uchun birinchi kirishdan so'ng parolni o'zgartirishingiz tavsiya etiladi.");

            await LoadUsersAsync();
            _rowCountLabel.Text = $"{_grid.RowCount} ta yozuv";

            MessageBox.Show(
                this,
                sent
                    ? $"Foydalanuvchi yaratildi. Login va parol Telegram orqali {user.FullName}ga yuborildi."
                    : $"Foydalanuvchi yaratildi, lekin Telegram orqali xabar yuborib bo'lmadi.\n\nLogin: {user.Username}\nParol: {generatedPassword}\n\nBuni foydalanuvchiga qo'lda yetkazing.",
                "Foydalanuvchi qo'shildi",
                MessageBoxButtons.OK,
                sent ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Foydalanuvchi qo'shishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Two SuperAdmin actions on an existing account, both picked from the grid and never retyped:
    /// account recovery (login/password reset) for someone who forgot their credentials, and changing their
    /// role.</summary>
    private async void OnEditUserClick(object? sender, EventArgs e)
    {
        if (!_editUserButton.Visible)
        {
            return;
        }

        var rowIndex = _grid.CurrentCell?.RowIndex ?? -1;
        if (rowIndex < 0 || rowIndex >= _currentUsers.Count)
        {
            MessageBox.Show(this, "Avval foydalanuvchini tanlang.", "Foydalanuvchi tanlanmagan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var user = _currentUsers[rowIndex];
        using var dialog = new EditUserForm(user);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _userService.EditUserAsync(user.Id, dialog.Username, dialog.FullName, string.IsNullOrEmpty(dialog.NewPassword) ? null : dialog.NewPassword);
            if (dialog.Role != user.Role)
            {
                await _userService.ChangeRoleAsync(user.Id, dialog.Role);
            }

            await LoadUsersAsync();
            _rowCountLabel.Text = $"{_grid.RowCount} ta yozuv";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Foydalanuvchini tahrirlashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Bulk-deletes every <see cref="Domain.Entities.HistoryEntry"/> — this table logs every queue/
    /// timer event and has no retention limit, so it's the one place in the app that grows unbounded and
    /// needs a manual way to clear it out.</summary>
    private async void OnClearHistoryClick(object? sender, EventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "Jurnaldagi barcha yozuvlar butunlay o'chiriladi. Bu amalni orqaga qaytarib bo'lmaydi. Davom etasizmi?",
            "Jurnalni tozalash",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _historyRepository.ClearAllAsync();
            await LoadHistoryAsync();
            _rowCountLabel.Text = $"{_grid.RowCount} ta yozuv";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Jurnalni tozalashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
