using System.Diagnostics;
using System.Drawing.Drawing2D;
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

    private readonly ListBox _sectionList;
    private readonly Label _sectionTitleLabel;
    private readonly Label _rowCountLabel;
    private readonly DataGridView _grid;
    private readonly RoundedButton _addUserButton;
    private readonly RoundedButton _openFileButton;
    private readonly ModernOutlineButton _refreshButton;

    /// <summary>Mirrors <see cref="_grid"/>'s bound rows, in the same order, whenever the "Taqdimotlar"
    /// section is loaded — the grid is bound to an anonymous-object projection (display-only) that loses
    /// <see cref="Presentation.FilePath"/>, so this is what a selected row is resolved back to for
    /// <see cref="OnOpenPresentationFileClick"/>. Left stale (not cleared) when another section is loaded,
    /// same as <c>AdminPanelForm._currentPresentations</c> — harmless since the button is hidden then.</summary>
    private List<Presentation> _currentPresentations = [];

    public SuperAdminPanelForm(
        ProjectService projectService,
        PresentationQueueService queueService,
        IPresenterRepository presenterRepository,
        JudgeService judgeService,
        UserService userService,
        CriterionService criterionService,
        ScoreService scoreService,
        IHistoryRepository historyRepository,
        IFileStorageService fileStorageService)
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
        _sectionList = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = LightColors.Panel,
            ForeColor = LightColors.TextPrimary,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10.5f),
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 46,
            Cursor = Cursors.Hand
        };
        _sectionList.Items.AddRange(Sections.Select(s => (object)s).ToArray());
        _sectionList.DrawItem += OnSectionDrawItem;
        _sectionList.SelectedIndexChanged += async (_, _) => await RefreshSelectedSectionAsync();
        _sectionList.MouseMove += (_, e) => SetHoveredIndex(_sectionList.IndexFromPoint(e.Location));
        _sectionList.MouseLeave += (_, _) => SetHoveredIndex(-1);

        var navWrap = new Panel { Dock = DockStyle.Left, Width = 232, BackColor = LightColors.Panel, Padding = new Padding(10, 16, 10, 16) };
        navWrap.Controls.Add(_sectionList);

        var navDivider = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = LightColors.Border };

        // ---------- Content: card header (section title + count + add-user) over the grid ----------
        var contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Background, Padding = new Padding(24) };

        var card = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Panel };
        card.Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(LightColors.Border), 0, 0, card.Width - 1, card.Height - 1);

        var cardHeader = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(20, 0, 16, 0) };
        var cardHeaderLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        cardHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cardHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cardHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));

        _sectionTitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = LightColors.TextPrimary
        };
        _rowCountLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = LightColors.TextSecondary
        };

        _addUserButton = new RoundedButton
        {
            Text = "+ Foydalanuvchi qo'shish",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 13, 0, 13),
            BackColor = LightColors.Success,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            CornerRadius = 8,
            Visible = false
        };
        _addUserButton.Click += OnAddUserClick;

        _openFileButton = new RoundedButton
        {
            Text = "📂 Faylni ochish",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 13, 0, 13),
            BackColor = LightColors.Accent,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            CornerRadius = 8,
            Visible = false
        };
        _openFileButton.Click += OnOpenPresentationFileClick;

        cardHeaderLayout.Controls.Add(_sectionTitleLabel, 0, 0);
        cardHeaderLayout.Controls.Add(_rowCountLabel, 1, 0);
        // Shares column 2 with _addUserButton — the two are mutually exclusive (only one section's action
        // button is ever visible at a time), so stacking them in the same cell and toggling Visible avoids
        // reworking the header into a 4-column layout for what's otherwise a single always-empty slot.
        cardHeaderLayout.Controls.Add(_addUserButton, 2, 0);
        cardHeaderLayout.Controls.Add(_openFileButton, 2, 0);
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
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OnOpenPresentationFileClick(null, EventArgs.Empty); };
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

    private int _hoveredSectionIndex = -1;

    /// <summary>ListBox never actually reports <see cref="DrawItemState.HotLight"/> for its own items (that
    /// flag is a menu/toolstrip thing) - hover has to be tracked by hand from MouseMove, same approach
    /// <see cref="PresentationPickerForm"/> uses for its list.</summary>
    private void SetHoveredIndex(int index)
    {
        if (index == _hoveredSectionIndex)
        {
            return;
        }

        var previous = _hoveredSectionIndex;
        _hoveredSectionIndex = index;
        if (previous >= 0 && previous < _sectionList.Items.Count)
        {
            _sectionList.Invalidate(_sectionList.GetItemRectangle(previous));
        }

        if (_hoveredSectionIndex >= 0 && _hoveredSectionIndex < _sectionList.Items.Count)
        {
            _sectionList.Invalidate(_sectionList.GetItemRectangle(_hoveredSectionIndex));
        }
    }

    /// <summary>Owner-draws each nav row as an icon + label with a rounded accent tint and left bar when
    /// selected, and a subtle hover tint otherwise — matching the rest of the app's dark, card-based look
    /// instead of a plain default-styled ListBox.</summary>
    private void OnSectionDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Sections.Length)
        {
            e.DrawBackground();
            return;
        }

        var (icon, label) = Sections[e.Index];
        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var isHot = e.Index == _hoveredSectionIndex;
        var bounds = e.Bounds;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bgBrush = new SolidBrush(LightColors.Panel))
        {
            g.FillRectangle(bgBrush, bounds);
        }

        var rowRect = Rectangle.Inflate(bounds, -2, -3);
        if (isSelected || isHot)
        {
            var fillColor = isSelected ? Blend(LightColors.Panel, LightColors.Accent, 0.14f) : LightColors.PanelAlt;
            using var path = RoundedRect(rowRect, 8f);
            using var fillBrush = new SolidBrush(fillColor);
            g.FillPath(fillBrush, path);
        }

        if (isSelected)
        {
            using var barBrush = new SolidBrush(LightColors.Accent);
            g.FillRectangle(barBrush, bounds.X, bounds.Y + 8, 4, bounds.Height - 16);
        }

        var iconRect = new Rectangle(bounds.X + 16, bounds.Y, 28, bounds.Height);
        TextRenderer.DrawText(g, icon, Font, iconRect, LightColors.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var labelRect = new Rectangle(bounds.X + 50, bounds.Y, bounds.Width - 60, bounds.Height);
        var labelFont = isSelected ? new Font(_sectionList.Font, FontStyle.Bold) : _sectionList.Font;
        TextRenderer.DrawText(g, label, labelFont, labelRect, isSelected ? LightColors.TextPrimary : LightColors.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    private static GraphicsPath RoundedRect(Rectangle rect, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private async Task RefreshSelectedSectionAsync()
    {
        if (_sectionList.SelectedItem is not (string icon, string label))
        {
            return;
        }

        _sectionTitleLabel.Text = $"{icon} {label}";
        _addUserButton.Visible = label == "Foydalanuvchilar";
        _openFileButton.Visible = label == "Taqdimotlar";

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

        _currentPresentations = presentations;
        _grid.DataSource = presentations
            .Select(p => new
            {
                Loyiha = projects.GetValueOrDefault(p.ProjectId, "?"),
                Taqdimotchi = p.FullName,
                Sarlavha = p.Title,
                Holat = UzbekText.StatusLabel(p.Status),
                QoshilganVaqt = p.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                Fayl = Path.GetFileName(p.FilePath)
            })
            .ToList();
    }

    /// <summary>Opens the selected presentation's uploaded file with the OS's default viewer — same mechanism
    /// as <c>AdminPanelForm.OnOpenPresentationFileClick</c>.</summary>
    private void OnOpenPresentationFileClick(object? sender, EventArgs e)
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
        var absolutePath = _fileStorageService.GetAbsolutePath(presentation.FilePath);
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
            _rowCountLabel.Text = $"{_grid.RowCount} ta yozuv";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Foydalanuvchi qo'shishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
