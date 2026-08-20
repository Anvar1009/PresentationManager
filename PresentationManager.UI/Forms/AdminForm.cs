using System.Drawing.Drawing2D;
using Microsoft.Extensions.Options;
using PresentationManager.ApiClient;
using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.TelegramBot;
using PresentationManager.UI.Controls;
using PresentationManager.UI.SlideDisplay;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>
/// Operator dashboard — every control surface lives here (queue reorder/search, app settings, starting a
/// presentation, and advancing it through discussion/next). Presentations normally arrive via the Telegram
/// bot, but the "Taqdimotlar" menu (<see cref="OnPresentationsClick"/>) also lets the operator add/edit/delete
/// them directly, across every project — not just whichever one is currently active in the live queue.
/// <see cref="PresentationForm"/> (Namoyish Ekrani) is left with no controls of its own: it's the window
/// actually shown or screen-shared to the audience, so it only ever displays the live timer and the slide —
/// nothing an operator clicks.
/// </summary>
public sealed class AdminForm : Form
{
    private readonly PresentationSessionController _session;
    private readonly IHistoryRepository _historyRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly PresentationQueueService _queueService;
    private readonly ProjectService _projectService;
    private readonly PresentationForm _presentationForm;
    private readonly AdminLinkService _adminLinkService;
    private readonly UserService _userService;
    private readonly OrderHubClient _orderHubClient;
    private readonly PresentationBotOptions _botOptions;

    /// <summary>Set via <see cref="SetCurrentUser"/> right after this singleton form is resolved from DI in
    /// Program.cs - lets <see cref="OnSettingsClick"/> pass this operator's own id through to
    /// <c>SettingsForm</c>'s "Botga ulash" button.</summary>
    private int? _currentUserId;

    /// <summary>Same instance handed to <see cref="SetCurrentUser"/> - kept around (and mutated in place by
    /// <see cref="UserMenuHelper"/> on a successful self-service login change) so <see cref="RefreshUserMenu"/>
    /// can rebuild the popup without re-fetching from the API.</summary>
    private User? _currentUser;

    /// <summary>Profile-info/Chiqish popup shown by <see cref="_userMenuButton"/>, at the bottom of the queue
    /// panel (see <see cref="BuildLeftQueuePanel"/>) - populated once <see cref="SetCurrentUser"/> runs.</summary>
    private readonly ContextMenuStrip _userMenu = new();
    private Button _userMenuButton = null!;

    private ListBox _queueListBox = null!;
    private TextBox _searchBox = null!;
    private List<Presentation> _displayedQueue = [];
    private ListBox _logsListBox = null!;
    private Point _queueDragStart;
    private Label _projectLabel = null!;

    /// <summary>Periodically reloads the active project's queue so presentations submitted via the Telegram
    /// bot show up on their own, without the operator having to do anything (on top of the explicit refresh
    /// already run right after the operator's own add/edit/delete via <see cref="OnPresentationsClick"/>).</summary>
    private readonly System.Windows.Forms.Timer _botPollTimer;

    private Label _currentNameLabel = null!;
    private Label _currentTitleLabel = null!;
    private Label _currentStatusLabel = null!;
    private PictureBox _previewBox = null!;
    private RoundedButton _startButton = null!;
    private RoundedButton _advanceButton = null!;
    private RoundedButton _hideScreenButton = null!;

    /// <summary>Id of whichever presentation <see cref="_previewBox"/> currently shows a thumbnail for —
    /// guards against re-rendering the same file's first page on every <see cref="RefreshCurrentPanel"/>
    /// call (fired on nearly every queue/status event), not just when the target actually changes.</summary>
    private int? _previewPresentationId;

    public AdminForm(
        PresentationSessionController session,
        IHistoryRepository historyRepository,
        ISettingsRepository settingsRepository,
        IFileStorageService fileStorageService,
        PresentationQueueService queueService,
        ProjectService projectService,
        PresentationForm presentationForm,
        AdminLinkService adminLinkService,
        UserService userService,
        OrderHubClient orderHubClient,
        IOptions<PresentationBotOptions> botOptions)
    {
        _session = session;
        _historyRepository = historyRepository;
        _settingsRepository = settingsRepository;
        _fileStorageService = fileStorageService;
        _queueService = queueService;
        _projectService = projectService;
        _presentationForm = presentationForm;
        _adminLinkService = adminLinkService;
        _userService = userService;
        _orderHubClient = orderHubClient;
        _botOptions = botOptions.Value;

        Text = "Taqdimotlar Boshqaruvi - Administrator";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(900, 600);

        BuildLayout();
        BuildMenu();
        WireSessionEvents();

        _botPollTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _botPollTimer.Tick += OnBotPollTick;
        _botPollTimer.Start();
        FormClosed += (_, _) => _botPollTimer.Stop();

        Load += OnFormLoad;
    }

    /// <summary>Called once, from Program.cs, right after this form is resolved from DI and before it's run -
    /// see <see cref="_currentUserId"/>.</summary>
    public void SetCurrentUser(User user)
    {
        _currentUserId = user.Id;
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

    /// <summary>No-op while no project is active or the DB is briefly unreachable - a missed tick just means
    /// a bot-submitted presentation shows up on the next one instead of instantly, which is an acceptable
    /// trade-off for not popping error dialogs on every transient hiccup.</summary>
    private async void OnBotPollTick(object? sender, EventArgs e)
    {
        if (_session.CurrentProjectId is null)
        {
            return;
        }

        try
        {
            await _session.ReloadQueueAsync();
        }
        catch
        {
            // Swallowed deliberately - see summary above.
        }
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = LightColors.Panel,
            ForeColor = LightColors.TextPrimary,
            Font = new Font("Segoe UI Semibold", 11.5f, FontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(12, 6, 8, 6),
            Renderer = new ToolStripProfessionalRenderer(new MenuStripColorTable())
        };

        var projectsItem = new ToolStripMenuItem("Loyihalar") { Padding = new Padding(14, 6, 14, 6), Margin = new Padding(0, 0, 4, 0) };
        projectsItem.Click += (_, _) => OnProjectsClick();
        menu.Items.Add(projectsItem);

        var presentationsItem = new ToolStripMenuItem("Taqdimotlar") { Padding = new Padding(14, 6, 14, 6), Margin = new Padding(0, 0, 4, 0) };
        presentationsItem.Click += (_, _) => OnPresentationsClick();
        menu.Items.Add(presentationsItem);

        var settingsItem = new ToolStripMenuItem("Sozlamalar") { Padding = new Padding(14, 6, 14, 6), Margin = new Padding(0, 0, 4, 0) };
        settingsItem.Click += (_, _) => OnSettingsClick();
        menu.Items.Add(settingsItem);

        MainMenuStrip = menu;
        Controls.Add(menu); // added after the Fill split layout so it correctly claims the top strip
    }

    /// <summary>Recolors <see cref="MenuStrip"/>'s hover/selected/pressed state to this dashboard's own
    /// accent instead of the OS's default Aero blue - <see cref="ToolStripProfessionalRenderer"/> otherwise
    /// paints selection with a fixed system-blue gradient regardless of the control's own BackColor/ForeColor,
    /// which looked out of place next to <see cref="LightColors"/>'s palette everywhere else in this form.</summary>
    private sealed class MenuStripColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => LightColors.Panel;
        public override Color MenuStripGradientEnd => LightColors.Panel;
        public override Color MenuItemSelected => LightColors.PanelAlt;
        public override Color MenuItemSelectedGradientBegin => LightColors.PanelAlt;
        public override Color MenuItemSelectedGradientEnd => LightColors.PanelAlt;
        public override Color MenuItemPressedGradientBegin => Blend(LightColors.Panel, LightColors.Accent, 0.16f);
        public override Color MenuItemPressedGradientEnd => Blend(LightColors.Panel, LightColors.Accent, 0.16f);
        public override Color MenuItemBorder => LightColors.Accent;
        public override Color MenuBorder => LightColors.Border;

        private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }

    private async void OnSettingsClick()
    {
        var current = await _settingsRepository.GetAsync();
        using var dialog = new SettingsForm(current, _adminLinkService, _botOptions, _currentUserId);
        dialog.ShowDialog(this);
        if (dialog.DialogResult == DialogResult.OK)
        {
            await _settingsRepository.SaveAsync(dialog.Result);
        }
    }

    /// <summary>Opens the Loyihalar dialog and reconciles whatever it decides the active project should be
    /// once it closes — reached either by explicitly activating a different project or, if the previously
    /// active one was just deleted, by that project id becoming null. Runs the same reconciliation
    /// regardless of DialogResult (Yopish vs. double-click activation) since both can change the answer.</summary>
    private async void OnProjectsClick()
    {
        try
        {
            using var dialog = new ProjectManagementForm(_projectService, _session.CurrentProjectId);
            dialog.ShowDialog(this);
            await ApplyActiveProjectAsync(dialog.SelectedActiveProjectId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Loyihalarda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Opens the cross-project "Taqdimotlar" dialog - any add/edit/delete there could affect the
    /// currently active project's own queue (or, for an add/edit into that project, wouldn't otherwise be
    /// reflected until the next <see cref="OnBotPollTick"/>), so the active queue is reloaded unconditionally
    /// once it closes, mirroring <see cref="OnProjectsClick"/>'s reconciliation.</summary>
    private async void OnPresentationsClick()
    {
        try
        {
            using var dialog = new PresentationManagementForm(_queueService, _projectService);
            dialog.ShowDialog(this);
            await _session.ReloadQueueAsync();
            RefreshQueueList();
            RefreshCurrentPanel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Taqdimotlarda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Switches the session to <paramref name="projectId"/> (only if it actually differs from what's
    /// already active), persists it as the operator's last-active project, and refreshes every panel that
    /// depends on the active project/queue.</summary>
    private async Task ApplyActiveProjectAsync(int? projectId)
    {
        if (projectId != _session.CurrentProjectId)
        {
            await _session.SetActiveProjectAsync(projectId);

            var settings = await _settingsRepository.GetAsync();
            settings.LastActiveProjectId = projectId;
            await _settingsRepository.SaveAsync(settings);
        }

        await RefreshProjectLabelAsync();
        RefreshQueueList();
        RefreshCurrentPanel();
    }

    private async Task RefreshProjectLabelAsync()
    {
        if (_session.CurrentProjectId is not int projectId)
        {
            _projectLabel.Text = "Loyiha tanlanmagan - \"Loyihalar\" menyusidan tanlang";
            return;
        }

        var projects = await _projectService.GetAllAsync();
        var project = projects.FirstOrDefault(p => p.Id == projectId);
        _projectLabel.Text = project is null ? "Loyiha tanlanmagan" : $"Loyiha: {project.Name}";
    }

    private async void OnFormLoad(object? sender, EventArgs e)
    {
        var primary = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        Bounds = primary.WorkingArea;

        var projects = await _projectService.GetAllAsync();
        var settings = await _settingsRepository.GetAsync();
        var startupProjectId = projects.Any(p => p.Id == settings.LastActiveProjectId)
            ? settings.LastActiveProjectId
            : null;

        await _session.InitializeAsync(startupProjectId);
        await RefreshProjectLabelAsync();
        RefreshQueueList();
        RefreshCurrentPanel();
        await RefreshLogsAsync();

        // No usable project yet (first launch, or the last-active one was deleted since) - send the
        // operator straight to Loyihalar instead of leaving them stuck on an empty, unexplained queue.
        if (startupProjectId is null)
        {
            OnProjectsClick();
        }

        // Namoyish Ekrani is not shown until the operator actually presses Boshlash — see OnStartClick.

        _orderHubClient.OrderRandomized += projectId => RunOnUiThread(() => _ = HandleOrderRandomizedAsync(projectId));
        try
        {
            await _orderHubClient.ConnectAsync();
        }
        catch
        {
            // Swallowed deliberately, same reasoning as OnBotPollTick: the 5s bot-poll timer already
            // guarantees eventual consistency, so a hub connection failure (server not yet redeployed with
            // the Hub, transient network issue, ...) just means order changes show up a few seconds late
            // instead of instantly, rather than blocking the operator's dashboard from loading at all.
        }
    }

    /// <summary>Fired by <see cref="_orderHubClient"/> the moment a "Tartib operatori" randomizes a
    /// project's presentation order - mirrors <see cref="OnBotPollTick"/>'s own <c>ReloadQueueAsync</c> call,
    /// just triggered instantly instead of on the next 5s tick.</summary>
    private async Task HandleOrderRandomizedAsync(int projectId)
    {
        if (projectId != _session.CurrentProjectId)
        {
            return;
        }

        try
        {
            await _session.ReloadQueueAsync();
        }
        catch
        {
            // Swallowed deliberately - see OnBotPollTick.
        }
    }

    private void WireSessionEvents()
    {
        // Escape on Namoyish Ekrani — the operator's only way back here whenever that screen is covering
        // this one (always true on a single monitor, since it fills and activates over the whole display —
        // see PresentationForm.PositionOnTargetMonitor) and the slide itself isn't visibly doing anything,
        // e.g. still black while a .pptx conversion is in progress or after it silently failed. Without this,
        // AdminForm's own "Ekranni yashirish" button is unreachable, since it lives on exactly the window
        // Namoyish Ekrani is hiding — indistinguishable from every other button in the panel "not working".
        _presentationForm.ReturnToAdminRequested += () => RunOnUiThread(() =>
        {
            _presentationForm.HideAll();
            Activate();
        });

        _session.PresentationChanged += () => RunOnUiThread(() =>
        {
            RefreshQueueList();
            RefreshCurrentPanel();
        });

        _session.StatusChanged += status => RunOnUiThread(() =>
        {
            RefreshCurrentPanel();
            _ = RefreshLogsAsync();

            // Waiting means the queue is exhausted (or, at startup, simply empty) — either way there's
            // nothing left to show on Namoyish Ekrani, so bring the operator back to the admin window.
            if (status == PresentationStatus.Waiting)
            {
                _presentationForm.HideAll();
                Activate();
            }

            // Deliberately no auto-triggered prompt here for DiscussionReady/ExtraDiscussionReady: when the
            // timer runs out on its own, the program must sit and wait — the operator is the only one who
            // opens the confirmation, by pressing "Keyingi" (see OnAdvanceClick). RefreshCurrentPanel above
            // already relabels that button ("MUHOKAMAGA O'TISH" / "QO'SHIMCHA VAQTNI BOSHLASH") so it's clear
            // what pressing it will do next.
        });
    }

    private void RunOnUiThread(Action action)
    {
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    // ---------- Layout ----------

    /// <summary>Was a fixed-size TableLayoutPanel grid (320px queue column, 190px logs row) — now
    /// SplitContainers instead so the operator can drag the queue/logs borders themselves. FixedPanel on
    /// each keeps the SAME panel pinned to its original absolute size when the whole form resizes (matching
    /// the old Absolute row/column), while the other side still gets the leftover space (matching the old
    /// Percent one) — only an explicit drag on the splitter itself changes the pinned side.</summary>
    private void BuildLayout()
    {
        var outerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = LightColors.Border,
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2
        };
        outerSplit.Panel1.BackColor = LightColors.Background;
        outerSplit.Panel2.BackColor = LightColors.Background;

        var innerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = LightColors.Border,
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel1
        };
        innerSplit.Panel1.BackColor = LightColors.Background;
        innerSplit.Panel2.BackColor = LightColors.Background;

        innerSplit.Panel1.Controls.Add(BuildLeftQueuePanel());
        innerSplit.Panel2.Controls.Add(BuildCenterPanel());

        outerSplit.Panel1.Controls.Add(innerSplit);
        outerSplit.Panel2.Controls.Add(BuildLogsPanel());

        Controls.Add(outerSplit);

        // Panel1MinSize/Panel2MinSize/SplitterDistance all validate against the container's CURRENT size
        // the moment they're set — setting any of them here in the constructor throws, since both
        // SplitContainers are still at their tiny design-time default size (not yet Dock-laid-out against
        // the real form). Deferred entirely to Shown, once OnFormLoad's Bounds assignment has already given
        // the form (and so these, via Dock=Fill) their real, final size.
        Shown += (_, _) =>
        {
            innerSplit.Panel1MinSize = 240;
            innerSplit.Panel2MinSize = 400;
            innerSplit.SplitterDistance = 320;

            outerSplit.Panel2MinSize = 100;
            outerSplit.SplitterDistance = Math.Max(outerSplit.Panel1MinSize, outerSplit.Height - 190 - outerSplit.SplitterWidth);
        };
    }

    // Cached (never disposed — these live for the app's lifetime, same as any other static UI constant) so
    // OnQueueDrawItem isn't allocating a fresh Font on every row repaint.
    private static readonly Font QueueNameFont = new("Segoe UI", 12.5f, FontStyle.Bold);
    private static readonly Font QueueTitleFont = new("Segoe UI", 9.5f, FontStyle.Italic);
    private static readonly Font QueueMetaFont = new("Segoe UI", 8.5f, FontStyle.Regular);
    private static readonly Font QueueHeaderFont = new("Segoe UI", 8f, FontStyle.Bold);

    private Control BuildLeftQueuePanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Panel, Padding = new Padding(14) };

        var header = new Label
        {
            Text = "TAQDIMOTLAR NAVBATI",
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = LightColors.TextSecondary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        _projectLabel = new Label
        {
            Text = "Loyiha tanlanmagan",
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = LightColors.Accent,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        // A search box wrapper that owns its own top/bottom gap directly (rather than relying on Margin,
        // which sibling Dock.Top controls don't reliably honor) so the table header below it never crowds it.
        var searchWrap = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(0, 8, 0, 28) };
        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Taqdimotchini qidirish...",
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10f)
        };
        _searchBox.TextChanged += (_, _) => RefreshQueueList();
        searchWrap.Controls.Add(_searchBox);

        // Column-header row so the queue reads like a table instead of a plain list, matching how
        // OnQueueDrawItem lays each row out below it.
        var tableHeader = new Panel { Dock = DockStyle.Top, Height = 24 };
        var headerName = new Label
        {
            Text = "TAQDIMOTCHI",
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = LightColors.TextSecondary,
            Font = QueueHeaderFont
        };
        tableHeader.Controls.Add(headerName);

        var tableHeaderRule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = LightColors.Border, Margin = new Padding(0, 0, 0, 4) };

        var selectHint = new Label
        {
            Text = "2 marta bosing - joriy/qayta tanlash",
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = LightColors.TextSecondary,
            Font = new Font("Segoe UI", 8f, FontStyle.Italic)
        };

        _queueListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 9.5f),
            AllowDrop = true,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 58
        };
        _queueListBox.MouseDown += OnQueueMouseDown;
        _queueListBox.MouseMove += OnQueueMouseMove;
        _queueListBox.DragOver += (_, e) => e.Effect = DragDropEffects.Move;
        _queueListBox.DragDrop += OnQueueDragDrop;
        _queueListBox.DoubleClick += (_, _) => OnQueueItemActivate();
        _queueListBox.DrawItem += OnQueueDrawItem;
        // A plain single click (just highlighting a row) needs to re-enable/point BOSHLASH at that row
        // too — see OnStartClick/RefreshCurrentPanel, which now start whatever's selected here rather than
        // only whatever the session already considers "current".
        _queueListBox.SelectedIndexChanged += (_, _) => RefreshCurrentPanel();

        // Populated once the logged-in user is known - see SetCurrentUser. Bottom of this queue panel (the
        // form's own "left column") rather than up in the menu strip, matching SuperAdminPanelForm's own
        // account info/Chiqish placement at the foot of its nav column.
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
        // AboveRight, not the default below-the-control placement: this panel sits flush with the bottom of
        // a maximized window, so a downward-opening popup would routinely be clipped by (or fall behind) the
        // taskbar.
        _userMenuButton.Click += (_, _) => _userMenu.Show(_userMenuButton, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
        var userMenuWrap = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 12, 0, 0) };
        userMenuWrap.Controls.Add(_userMenuButton);

        panel.Controls.Add(_queueListBox);
        panel.Controls.Add(userMenuWrap);
        panel.Controls.Add(selectHint);
        panel.Controls.Add(tableHeaderRule);
        panel.Controls.Add(tableHeader);
        panel.Controls.Add(searchWrap);
        panel.Controls.Add(_projectLabel);
        panel.Controls.Add(header);
        return panel;
    }

    /// <summary>Paints each queue row as a mini table: order number, a larger bold presenter name with the
    /// title underneath — replacing the old plain "1. Name - Title [Status]" text line. Reads
    /// <see cref="_displayedQueue"/> directly by <c>e.Index</c> rather than casting <c>Items[e.Index]</c>,
    /// since Items still holds the plain-text fallback strings RefreshQueueList populates (kept for
    /// screen-reader/typeahead purposes) rather than Presentation objects.</summary>
    private void OnQueueDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _displayedQueue.Count)
        {
            e.DrawBackground();
            return;
        }

        var p = _displayedQueue[e.Index];
        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var isCurrent = p == _session.CurrentPresentation;
        var bounds = e.Bounds;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rowColor = isSelected
            ? Blend(LightColors.PanelAlt, LightColors.Accent, 0.30f)
            : e.Index % 2 == 0
                ? LightColors.PanelAlt
                : Blend(LightColors.PanelAlt, LightColors.Panel, 0.5f);
        using (var bgBrush = new SolidBrush(rowColor))
        {
            g.FillRectangle(bgBrush, bounds);
        }

        if (isCurrent)
        {
            using var barBrush = new SolidBrush(LightColors.Success);
            g.FillRectangle(barBrush, bounds.X, bounds.Y, 4, bounds.Height);
        }
        else if (isSelected)
        {
            using var barBrush = new SolidBrush(LightColors.Accent);
            g.FillRectangle(barBrush, bounds.X, bounds.Y, 4, bounds.Height);
        }

        var textLeft = bounds.X + 16;
        var orderRect = new Rectangle(textLeft, bounds.Y, 30, bounds.Height);
        TextRenderer.DrawText(g, $"{p.OrderNumber + 1}.", QueueMetaFont, orderRect, LightColors.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var nameRect = new Rectangle(textLeft + 28, bounds.Y + 7, bounds.Right - (textLeft + 28) - 16, 24);
        TextRenderer.DrawText(g, p.FullName, QueueNameFont, nameRect, LightColors.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var titleRect = new Rectangle(textLeft + 28, bounds.Y + 33, bounds.Width - (textLeft + 28) - 12, 20);
        TextRenderer.DrawText(g, p.Title, QueueTitleFont, titleRect, LightColors.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    private void OnQueueMouseDown(object? sender, MouseEventArgs e)
    {
        _queueDragStart = e.Button == MouseButtons.Left ? e.Location : Point.Empty;
    }

    /// <summary>Only starts the actual drag once the mouse has moved past the OS's drag threshold while
    /// held down — calling DoDragDrop unconditionally from MouseDown (the previous approach) fired it on
    /// every plain click, and DoDragDrop's own modal message loop swallowed the click before the ListBox's
    /// normal selection/SelectedIndexChanged processing ever got to it — so simply clicking a row (e.g. an
    /// old/Finished file, to re-present it) silently failed to select it, leaving BOSHLASH disabled.</summary>
    private void OnQueueMouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _queueDragStart == Point.Empty || _queueListBox.SelectedIndex < 0)
        {
            return;
        }

        var dragSize = SystemInformation.DragSize;
        var dragZone = new Rectangle(
            _queueDragStart.X - dragSize.Width / 2, _queueDragStart.Y - dragSize.Height / 2,
            dragSize.Width, dragSize.Height);

        if (!dragZone.Contains(e.Location))
        {
            _queueDragStart = Point.Empty;
            _queueListBox.DoDragDrop(_queueListBox.SelectedIndex, DragDropEffects.Move);
        }
    }

    private async void OnQueueDragDrop(object? sender, DragEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_searchBox.Text))
        {
            // Reordering while a search filter is active would only reorder the visible subset,
            // silently corrupting OrderNumber for everyone else - require the full, unfiltered list.
            MessageBox.Show(this, "Navbatni o'zgartirishdan oldin qidiruv maydonini tozalang.", "Tartiblash", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var clientPoint = _queueListBox.PointToClient(new Point(e.X, e.Y));
        var targetIndex = _queueListBox.IndexFromPoint(clientPoint);
        if (targetIndex < 0)
        {
            targetIndex = _displayedQueue.Count - 1;
        }

        var sourceIndex = (int)e.Data!.GetData(typeof(int))!;
        if (sourceIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var moved = _displayedQueue[sourceIndex];
        _displayedQueue.RemoveAt(sourceIndex);
        _displayedQueue.Insert(targetIndex, moved);

        try
        {
            await _queueService.ReorderAsync(_displayedQueue.Select(p => p.Id).ToList());
            await _session.ReloadQueueAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Tartiblashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Presentation? GetSelectedPresentation()
    {
        var index = _queueListBox.SelectedIndex;
        return index >= 0 && index < _displayedQueue.Count ? _displayedQueue[index] : null;
    }

    /// <summary>Double-clicking a queue entry (re-)selects it as the current presentation and resets it to
    /// Ready, regardless of its previous status — this is how an already-Finished presentation can be
    /// presented again (e.g. re-running the same file for a demo/test).</summary>
    private async void OnQueueItemActivate()
    {
        var selected = GetSelectedPresentation();
        if (selected is null)
        {
            return;
        }

        try
        {
            await _session.SelectPresentationAsync(selected.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Tanlashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Widest the center "current presentation" card's content is ever allowed to stretch to — only
    /// kicks in on a genuinely huge (4K+/ultra-wide) monitor, so the slide preview still fills essentially
    /// all of the available width everywhere else instead of leaving it visibly letterboxed by dead panel
    /// background on both sides. See <c>RepositionCardContent</c>.</summary>
    private const int CardContentMaxWidth = 1400;

    /// <summary>Fixed breathing margin kept on each side of the content even below <see cref="CardContentMaxWidth"/> —
    /// small enough that the preview still reads as filling the card, per the 24px outer-margin convention
    /// used throughout this dashboard.</summary>
    private const int CardContentSideMargin = 24;

    private Control BuildCenterPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Background, Padding = new Padding(20) };

        var card = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Panel };

        // Not Dock.Fill — kept at a sane reading width and re-centered on every resize (see
        // RepositionCardContent) instead of stretching edge-to-edge on a wide admin monitor.
        var content = new Panel { BackColor = LightColors.Panel, Padding = new Padding(24) };

        _currentNameLabel = new Label
        {
            Dock = DockStyle.Top, Height = 46,
            Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = LightColors.TextPrimary,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _currentTitleLabel = new Label
        {
            Dock = DockStyle.Top, Height = 32,
            Font = new Font("Segoe UI", 13, FontStyle.Italic), ForeColor = LightColors.Accent,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _currentStatusLabel = new Label
        {
            Dock = DockStyle.Top, Height = 34,
            Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = LightColors.Warning,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _startButton = new RoundedButton
        {
            Text = "BOSHLASH",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, 0),
            BackColor = LightColors.Success,
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        };
        _startButton.Click += (_, _) => OnStartClick();

        // Mirrors what used to be a floating button directly on Namoyish Ekrani (the projector/shared
        // screen) — moved here so that screen stays pure output (timer + slide only, no controls at all)
        // and every operator action happens from this dashboard instead.
        _advanceButton = new RoundedButton
        {
            Text = "KEYINGI",
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 6, 0),
            BackColor = LightColors.DiscussionAction,
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        };
        _advanceButton.Click += (_, _) => OnAdvanceClick();

        // Moved here from what used to be a top-menu item ("Namoyish ekranini yashirish") — grouped with
        // Boshlash/Keyingi so the operator has every action for the shared/projector screen in one row
        // instead of hunting through the menu strip.
        _hideScreenButton = new RoundedButton
        {
            Text = "NAMOYISH EKRANINI YOPISH",
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 0, 0),
            BackColor = LightColors.PanelAlt,
            Font = new Font("Segoe UI", 12, FontStyle.Bold)
        };
        _hideScreenButton.Click += (_, _) => _presentationForm.HideAll();

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 64, ColumnCount = 3, RowCount = 1 };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actionRow.Controls.Add(_startButton, 0, 0);
        actionRow.Controls.Add(_advanceButton, 1, 0);
        actionRow.Controls.Add(_hideScreenButton, 2, 0);

        var hint = new Label
        {
            Text = "Boshlangach, fayl Namoyish ekranida ochiladi - taymer shu yerda, ekranning o'zida hech qanday tugma bo'lmaydi.",
            Dock = DockStyle.Bottom,
            Height = 24,
            ForeColor = LightColors.TextSecondary,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            TextAlign = ContentAlignment.MiddleCenter
        };

        // Framed the same way the queue/logs panels frame their own content (PanelAlt fill, small gap from
        // the labels above and the hint/button below) so the preview reads as its own "slide" rather than a
        // random image floating on the card's background.
        var previewWrap = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Panel, Padding = new Padding(0, 10, 0, 10) };
        _previewBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = LightColors.PanelAlt,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle
        };
        previewWrap.Controls.Add(_previewBox);

        // Added first: Dock layout claims space in reverse of Add order, so this Fill panel (added first,
        // laid out last) only ever gets whatever's left after the Top labels and Bottom hint/button above
        // and below it have already taken their strips — see BuildLeftQueuePanel's _queueListBox for the
        // same established pattern in this file.
        content.Controls.Add(previewWrap);
        content.Controls.Add(hint);
        content.Controls.Add(actionRow);
        content.Controls.Add(_currentStatusLabel);
        content.Controls.Add(_currentTitleLabel);
        content.Controls.Add(_currentNameLabel);

        card.Controls.Add(content);

        void RepositionCardContent()
        {
            var width = Math.Min(CardContentMaxWidth, Math.Max(0, card.ClientSize.Width - CardContentSideMargin * 2));
            content.SetBounds((card.ClientSize.Width - width) / 2, 0, width, card.ClientSize.Height);
        }

        card.Resize += (_, _) => RepositionCardContent();
        RepositionCardContent();

        panel.Controls.Add(card);
        return panel;
    }

    private Control BuildLogsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Panel, Padding = new Padding(10) };

        var header = new Label
        {
            Text = "JURNAL",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = LightColors.TextSecondary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        _logsListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextSecondary,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            Font = new Font("Consolas", 9f)
        };

        panel.Controls.Add(_logsListBox);
        panel.Controls.Add(header);
        return panel;
    }

    // ---------- Refresh ----------

    private void RefreshQueueList()
    {
        var filter = _searchBox.Text.Trim();
        _displayedQueue = string.IsNullOrEmpty(filter)
            ? _session.Queue.ToList()
            : _session.Queue.Where(p => p.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        // Rebuilding Items always resets SelectedIndex to -1 — preserve whatever the operator had picked
        // (by Id, since row positions can shift) so a PresentationChanged/StatusChanged refresh landing
        // between "click a row" and "press Boshlash" doesn't silently drop their selection.
        var selectedId = GetSelectedPresentation()?.Id;

        _queueListBox.BeginUpdate();
        _queueListBox.Items.Clear();
        foreach (var p in _displayedQueue)
        {
            var marker = p == _session.CurrentPresentation ? "> " : "   ";
            _queueListBox.Items.Add($"{marker}{p.OrderNumber + 1}. {p.FullName} - {p.Title}  [{UzbekText.StatusLabel(p.Status)}]");
        }

        if (selectedId is not null)
        {
            var restoredIndex = _displayedQueue.FindIndex(p => p.Id == selectedId);
            if (restoredIndex >= 0)
            {
                _queueListBox.SelectedIndex = restoredIndex;
            }
        }

        // Queue exhausted (nothing current) and nothing manually selected — default to the last entry so
        // BOSHLASH is immediately clickable to re-present it, instead of leaving the operator with every
        // row unselected and no obvious way to restart what just finished.
        if (_queueListBox.SelectedIndex < 0 && _session.CurrentPresentation is null && _displayedQueue.Count > 0)
        {
            _queueListBox.SelectedIndex = _displayedQueue.Count - 1;
        }
        _queueListBox.EndUpdate();
    }

    private void RefreshCurrentPanel()
    {
        var current = _session.CurrentPresentation;

        // BOSHLASH targets whatever row is highlighted in the queue, not just the session's own "current"
        // pointer — otherwise a Finished/Skipped file that the operator clicks on (to run it again) left
        // the button disabled forever, since CurrentPresentation only ever advances forward on its own.
        // Any explicitly selected row is always startable, even one that happens to still be "current" —
        // e.g. parked mid-flow at Discussion/Paused/DiscussionReady after the operator backed out of
        // advancing past it — since OnStartClick always resets the target back to Ready before starting
        // regardless. With nothing selected, fall back to the original "only when idle" rule so the button
        // doesn't sit falsely enabled mid-presentation with no explicit choice made.
        var selected = GetSelectedPresentation();

        // Same idea applies to the name/title/preview above the button: once the operator clicks a
        // different row to re-present it, this should reflect THAT file immediately, not whatever the
        // session still calls "current" — otherwise the name and thumbnail would keep pointing at the old
        // one right up until Boshlash is actually pressed.
        var target = selected ?? current;
        _currentNameLabel.Text = target?.FullName ?? "Taqdimotchi yuklanmagan";
        _currentTitleLabel.Text = target?.Title ?? string.Empty;
        _currentStatusLabel.Text = UzbekText.StatusLabel(_session.Status);

        _startButton.Enabled = selected is not null
            || (current is not null && _session.Status is PresentationStatus.Waiting or PresentationStatus.Ready);

        // Mirrors the old floating control button on Namoyish Ekrani — always acts on the session's actual
        // active presentation (never the merely-selected queue row, unlike BOSHLASH above), since it's
        // advancing whatever's really running rather than jumping to something else.
        _advanceButton.Enabled = current is not null;
        _advanceButton.Text = _session.Status switch
        {
            PresentationStatus.Running or PresentationStatus.Paused => "MUHOKAMAGA O'TISH",
            PresentationStatus.ExtraDiscussionReady => "QO'SHIMCHA VAQTNI BOSHLASH",
            _ => "KEYINGI"
        };

        _ = UpdatePreviewAsync(target);
    }

    /// <summary>Renders <paramref name="target"/>'s first page into <see cref="_previewBox"/> — the same
    /// file BOSHLASH would open — so the operator can visually confirm which presentation is about to start
    /// before pressing it. Guarded by <see cref="_previewPresentationId"/> so the (relatively slow, for
    /// .pptx) render only runs once per distinct target, not on every one of RefreshCurrentPanel's frequent
    /// calls, and bails out quietly if the selection has already moved on by the time it finishes.</summary>
    private async Task UpdatePreviewAsync(Presentation? target)
    {
        if (target is null)
        {
            _previewPresentationId = null;
            var empty = _previewBox.Image;
            _previewBox.Image = null;
            empty?.Dispose();
            return;
        }

        if (_previewPresentationId == target.Id)
        {
            return;
        }
        _previewPresentationId = target.Id;

        var absolutePath = await _fileStorageService.GetAbsolutePathAsync(target.FilePath);
        var thumbnail = await SlideThumbnailService.GetFirstPageThumbnailAsync(
            absolutePath, target.FileType, _previewBox.Width, _previewBox.Height);

        if (_previewPresentationId != target.Id || _previewBox.IsDisposed)
        {
            thumbnail?.Dispose();
            return;
        }

        var previous = _previewBox.Image;
        _previewBox.Image = thumbnail;
        previous?.Dispose();
    }

    /// <summary>Starts whatever's selected in the queue list — including a Finished/Skipped file the
    /// operator wants to (re-)present — falling back to the session's own current pointer only when
    /// nothing's been clicked yet (e.g. right after launch). Re-selecting first resets any prior status
    /// back to Ready, which is what makes starting an old/already-finished file work — needed not only when
    /// switching to a different target, but also when the target is already "current" yet parked mid-flow
    /// (Discussion/Paused/DiscussionReady, e.g. after backing out of advancing past it): without resetting,
    /// StartPresentationAsync would silently refuse to start since it only proceeds from Ready/Waiting.</summary>
    private async void OnStartClick()
    {
        try
        {
            var target = GetSelectedPresentation() ?? _session.CurrentPresentation;
            if (target is null)
            {
                return;
            }

            var alreadyCurrentAndReady = _session.CurrentPresentation?.Id == target.Id
                && _session.Status is PresentationStatus.Ready or PresentationStatus.Waiting;
            if (!alreadyCurrentAndReady)
            {
                await _session.SelectPresentationAsync(target.Id);
            }

            await _presentationForm.StartSelectedPresentationAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Boshlashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>This button does one of three different things depending on <see cref="PresentationSessionController.Status"/>
    /// — see <see cref="RefreshCurrentPanel"/> for the matching label on each. Moved here from what used to
    /// be a floating button directly on Namoyish Ekrani, so that screen stays pure output.</summary>
    private async void OnAdvanceClick()
    {
        try
        {
            if (_session.Status == PresentationStatus.DiscussionReady)
            {
                // The timer running out only ever reaches this status on its own — it never pops the
                // confirmation itself (see WireSessionEvents). The operator's "Keyingi" click here is what
                // actually opens the prompt.
                await ConfirmAndStartDiscussionAsync();
                return;
            }

            if (_session.Status == PresentationStatus.ExtraDiscussionReady)
            {
                // Same as the DiscussionReady branch above, for the optional extra phase.
                await ConfirmAndStartExtraDiscussionAsync();
                return;
            }

            var inDiscussion = _session.ActiveTimerMode is TimerMode.Discussion or TimerMode.ExtraDiscussion;

            if (!inDiscussion)
            {
                // Only moves the status to DiscussionReady — the slide/timer on Namoyish Ekrani stays
                // exactly as it is, and the discussion clock itself only starts once the operator confirms
                // the follow-up prompt that fires automatically once that status is reached.
                var confirm = MessageBox.Show(this,
                    "Muhokamaga o'tishni xohlaysizmi?",
                    "Tasdiqlash", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }

                await _session.NextPresenterAsync();
                return;
            }

            var remainingSeconds = _session.ActiveTimerMode == TimerMode.ExtraDiscussion
                ? _session.ExtraDiscussionRemainingSeconds
                : _session.DiscussionRemainingSeconds;

            if (remainingSeconds > 0)
            {
                var confirm = MessageBox.Show(this,
                    "Muhokama vaqti hali tugamagan. Keyingi taqdimotchiga o'tishni xohlaysizmi?",
                    "Tasdiqlash", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            // Deliberately doesn't finish/touch the current presentation until the picker below actually
            // returns a choice — so Namoyish Ekrani stays exactly as it was for as long as the picker sits
            // open, and a cancelled picker leaves discussion running untouched instead of stranding the
            // operator on a blank screen.
            await ShowNextPresentationPickerAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Amal bajarilmadi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>The one required confirmation before the discussion clock actually starts ticking — only
    /// ever opened by the operator's own "Keyingi" click once <see cref="PresentationStatus.DiscussionReady"/>
    /// is reached (see <see cref="OnAdvanceClick"/>), never popped on its own. Declining leaves the state
    /// parked exactly as it was; clicking "Keyingi" again just re-asks.</summary>
    private async Task ConfirmAndStartDiscussionAsync()
    {
        var confirm = MessageBox.Show(this,
            "Muhokama vaqtini boshlashni tasdiqlaysizmi?",
            "Muhokamani boshlash", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        await _session.StartDiscussionAsync();
    }

    /// <summary>The optional extra-time counterpart to <see cref="ConfirmAndStartDiscussionAsync"/> — only
    /// ever opened by the operator's own "Keyingi" click once <see cref="PresentationStatus.ExtraDiscussionReady"/>
    /// is reached (see <see cref="OnAdvanceClick"/>), which only happens when the presentation actually has
    /// extra discussion time configured. Declining immediately offers the other realistic action (skip the
    /// extra time and move on) instead of stranding the operator on a dead end.</summary>
    private async Task ConfirmAndStartExtraDiscussionAsync()
    {
        var confirm = MessageBox.Show(this,
            "Qo'shimcha muhokama vaqtini boshlashni tasdiqlaysizmi?",
            "Qo'shimcha muhokama", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm == DialogResult.Yes)
        {
            await _session.StartExtraDiscussionAsync();
            return;
        }

        var moveNext = MessageBox.Show(this,
            "Qo'shimcha vaqtsiz keyingi taqdimotchiga o'tishni xohlaysizmi?",
            "Keyingisiga o'tish", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (moveNext == DialogResult.Yes)
        {
            await _session.NextPresenterAsync();
        }
    }

    /// <summary>Shown once a discussion is finished — lets the operator choose which queued presentation
    /// starts next instead of the queue silently advancing on its own. The actual finish/select/start
    /// sequence deliberately runs here, after <c>ShowDialog</c> returns, rather than from inside the
    /// picker's own button click — running those (real async DB + WebView2) calls from a handler nested
    /// inside the dialog's own modal message loop made the presentation silently fail to start once
    /// picked.</summary>
    private async Task ShowNextPresentationPickerAsync()
    {
        using var picker = new PresentationPickerForm(_session);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedPresentationId is not { } presentationId)
        {
            return;
        }

        await _session.FinishCurrentPresentationAsync();
        await _session.SelectPresentationAsync(presentationId);
        await _presentationForm.StartSelectedPresentationAsync();
    }

    private async Task RefreshLogsAsync()
    {
        var recent = await _historyRepository.GetRecentAsync(100);
        recent.Reverse(); // repository returns newest-first; display oldest-to-newest, newest at the bottom

        _logsListBox.BeginUpdate();
        _logsListBox.Items.Clear();
        foreach (var entry in recent)
        {
            _logsListBox.Items.Add($"[{entry.Timestamp:HH:mm:ss}] {entry.Message}");
        }
        _logsListBox.EndUpdate();

        if (_logsListBox.Items.Count > 0)
        {
            _logsListBox.TopIndex = _logsListBox.Items.Count - 1;
        }
    }
}
