using PresentationManager.ApiClient;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.UI.Controls;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>"Tartib operatori" role's dashboard - the narrowest of the four: pick a project, click one
/// button to randomly shuffle its presentation order (see
/// <see cref="PresentationQueueService.RandomizeOrderAsync"/>), which the API then broadcasts live over
/// SignalR to AdminForm's queue view. No other project/criteria/judge management, matching
/// <see cref="Domain.Enums.UserRole.OrderOperator"/>'s deliberately narrow scope.</summary>
public sealed class OrderOperatorForm : Form
{
    private readonly ProjectService _projectService;
    private readonly OrderRandomizerClient _orderRandomizerClient;
    private readonly UserService _userService;

    private readonly ComboBox _projectCombo;
    private readonly RoundedButton _randomizeButton;
    private readonly Label _statusLabel;

    /// <summary>Profile-info/Chiqish popup shown by <see cref="_userMenuButton"/> - populated once the
    /// logged-in user is known, see <see cref="SetCurrentUser"/>.</summary>
    private readonly ContextMenuStrip _userMenu = new();
    private readonly Button _userMenuButton;

    /// <summary>Same instance handed to <see cref="SetCurrentUser"/> - kept around (and mutated in place by
    /// <see cref="UserMenuHelper"/> on a successful self-service login change) so <see cref="RefreshUserMenu"/>
    /// can rebuild the popup without re-fetching from the API.</summary>
    private User? _currentUser;

    private Project? SelectedProject => _projectCombo.SelectedItem as Project;

    public OrderOperatorForm(ProjectService projectService, OrderRandomizerClient orderRandomizerClient, UserService userService)
    {
        _projectService = projectService;
        _orderRandomizerClient = orderRandomizerClient;
        _userService = userService;

        Text = "Tartib operatori paneli";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 10.5f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 420);
        WindowState = FormWindowState.Maximized;

        // ---------- Top header ----------
        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = LightColors.Panel, Padding = new Padding(28, 0, 24, 0) };
        var headerBottomRule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = LightColors.Border };
        var titleLabel = new Label
        {
            Text = "Tartib operatori paneli",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = LightColors.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(titleLabel);

        // ---------- Bottom bar: account info / Chiqish ----------
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
        _userMenuButton.Click += (_, _) => _userMenu.Show(_userMenuButton, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
        bottomPanel.Controls.Add(_userMenuButton);

        // ---------- Center content: a single card with project picker + the one action ----------
        var contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Background, Padding = new Padding(24) };
        var card = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Panel, Padding = new Padding(48) };
        card.Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(LightColors.Border), 0, 0, card.Width - 1, card.Height - 1);

        _statusLabel = new Label
        {
            Text = string.Empty,
            Dock = DockStyle.Top,
            Height = 28,
            Margin = new Padding(0, 16, 0, 0),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = LightColors.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _randomizeButton = new RoundedButton
        {
            Text = "🔀 Tartibni aralashtirish",
            Dock = DockStyle.Top,
            Height = 56,
            Margin = new Padding(0, 24, 0, 0),
            BackColor = LightColors.Accent,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            CornerRadius = 10
        };
        _randomizeButton.Click += OnRandomizeClick;

        _projectCombo = new ComboBox
        {
            Dock = DockStyle.Top,
            Height = 40,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(Project.Name),
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            Font = new Font("Segoe UI", 11.5f)
        };

        var projectLabel = new Label
        {
            Text = "Loyiha:",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = LightColors.TextSecondary,
            Font = new Font("Segoe UI", 10f)
        };

        var subtitleLabel = new Label
        {
            Text = "Loyihani tanlang va taqdimotchilar tartibini tasodifiy tarzda belgilang.",
            Dock = DockStyle.Top,
            Height = 28,
            Margin = new Padding(0, 0, 0, 24),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = LightColors.TextSecondary
        };

        var cardTitleLabel = new Label
        {
            Text = "Taqdimotlar tartibini belgilash",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = LightColors.TextPrimary
        };

        // Docking stacks in reverse Controls-add order (last added ends up closest to the Top edge) - same
        // convention used throughout this app's other forms (e.g. SuperAdminPanelForm's title/subtitle stack).
        card.Controls.Add(_statusLabel);
        card.Controls.Add(_randomizeButton);
        card.Controls.Add(_projectCombo);
        card.Controls.Add(projectLabel);
        card.Controls.Add(subtitleLabel);
        card.Controls.Add(cardTitleLabel);
        contentPanel.Controls.Add(card);

        Controls.Add(contentPanel);
        Controls.Add(bottomPanel);
        Controls.Add(headerBottomRule);
        Controls.Add(header);

        Load += async (_, _) => await LoadProjectsAsync();
    }

    /// <summary>Called once, from Program.cs, right after this form is resolved from DI and before it's run -
    /// mirrors AdminForm/AdminPanelForm/SuperAdminPanelForm's own <c>SetCurrentUser</c>.</summary>
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

    private async Task LoadProjectsAsync()
    {
        var projects = await _projectService.GetAllAsync();
        _projectCombo.DataSource = projects;
        _randomizeButton.Enabled = projects.Count > 0;
        if (projects.Count == 0)
        {
            _statusLabel.Text = "Hozircha loyihalar yo'q.";
        }
    }

    private async void OnRandomizeClick(object? sender, EventArgs e)
    {
        var project = SelectedProject;
        if (project is null)
        {
            MessageBox.Show(this, "Avval loyihani tanlang.", "Loyiha tanlanmagan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _randomizeButton.Enabled = false;
        _statusLabel.Text = "Tartib belgilanmoqda...";

        try
        {
            await _orderRandomizerClient.RandomizeOrderAsync(project.Id);
            _statusLabel.Text = $"\"{project.Name}\" loyihasi uchun tartib tasodifiy belgilandi.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = string.Empty;
            MessageBox.Show(this, ex.Message, "Tartib belgilashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _randomizeButton.Enabled = true;
        }
    }
}
