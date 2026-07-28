using System.Drawing.Drawing2D;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.UI.Controls;
using PresentationManager.UI.Localization;
using PresentationManager.UI.SlideDisplay;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>
/// Operator dashboard, now scoped to just managing the presentation queue (add/edit/delete/reorder/search)
/// and app settings. Starting, advancing and watching the timer all happen on <see cref="PresentationForm"/>
/// instead — see that class for the control surface.
/// </summary>
public sealed class AdminForm : Form
{
    private readonly PresentationSessionController _session;
    private readonly IHistoryRepository _historyRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly PresentationQueueService _queueService;
    private readonly PresentationForm _presentationForm;

    private ListBox _queueListBox = null!;
    private TextBox _searchBox = null!;
    private List<Presentation> _displayedQueue = [];
    private ListBox _logsListBox = null!;
    private Point _queueDragStart;

    private Label _currentNameLabel = null!;
    private Label _currentTitleLabel = null!;
    private Label _currentStatusLabel = null!;
    private PictureBox _previewBox = null!;
    private RoundedButton _startButton = null!;

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
        PresentationForm presentationForm)
    {
        _session = session;
        _historyRepository = historyRepository;
        _settingsRepository = settingsRepository;
        _fileStorageService = fileStorageService;
        _queueService = queueService;
        _presentationForm = presentationForm;

        Text = "Taqdimotlar Boshqaruvi - Administrator";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(900, 600);

        BuildLayout();
        BuildMenu();
        WireSessionEvents();

        Load += OnFormLoad;
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip { BackColor = AppColors.Panel, ForeColor = AppColors.TextPrimary };
        var settingsItem = new ToolStripMenuItem("Sozlamalar");
        settingsItem.Click += (_, _) => OnSettingsClick();
        menu.Items.Add(settingsItem);
        MainMenuStrip = menu;
        Controls.Add(menu); // added after the Fill split layout so it correctly claims the top strip
    }

    private async void OnSettingsClick()
    {
        var current = await _settingsRepository.GetAsync();
        using var dialog = new SettingsForm(current);
        dialog.ShowDialog(this);
        if (dialog.DialogResult == DialogResult.OK)
        {
            await _settingsRepository.SaveAsync(dialog.Result);
        }
    }

    private async void OnFormLoad(object? sender, EventArgs e)
    {
        var primary = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        Bounds = primary.WorkingArea;

        await _session.InitializeAsync();
        RefreshQueueList();
        RefreshCurrentPanel();
        await RefreshLogsAsync();

        // Namoyish Ekrani is not shown until the operator actually presses Boshlash — see OnStartClick.
    }

    private void WireSessionEvents()
    {
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
            BackColor = AppColors.Border,
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2
        };
        outerSplit.Panel1.BackColor = AppColors.Background;
        outerSplit.Panel2.BackColor = AppColors.Background;

        var innerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = AppColors.Border,
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel1
        };
        innerSplit.Panel1.BackColor = AppColors.Background;
        innerSplit.Panel2.BackColor = AppColors.Background;

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
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppColors.Panel, Padding = new Padding(14) };

        var header = new Label
        {
            Text = "TAQDIMOTLAR NAVBATI",
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = AppColors.TextSecondary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        // A search box wrapper that owns its own top/bottom gap directly (rather than relying on Margin,
        // which sibling Dock.Top controls don't reliably honor) so the toolbar below it never crowds it.
        var searchWrap = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(0, 8, 0, 22) };
        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Taqdimotchini qidirish...",
            BackColor = AppColors.PanelAlt,
            ForeColor = AppColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10f)
        };
        _searchBox.TextChanged += (_, _) => RefreshQueueList();
        searchWrap.Controls.Add(_searchBox);

        // Same wrapper-owns-its-gap trick below the toolbar, so the table header/list underneath always
        // sits a clear, fixed distance away regardless of what else changes around it.
        var toolbarWrap = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(0, 0, 0, 20) };
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (var i = 0; i < 3; i++)
        {
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        }

        var addButton = ToolbarButton("+ Qo'shish", AppColors.Success);
        addButton.Click += (_, _) => OnAddClick();
        var editButton = ToolbarButton("Tahrirlash", AppColors.Accent);
        editButton.Click += (_, _) => OnEditClick();
        var deleteButton = ToolbarButton("O'chirish", AppColors.Danger);
        deleteButton.Click += (_, _) => OnDeleteClick();

        toolbar.Controls.Add(addButton, 0, 0);
        toolbar.Controls.Add(editButton, 1, 0);
        toolbar.Controls.Add(deleteButton, 2, 0);
        toolbarWrap.Controls.Add(toolbar);

        // Column-header row so the queue reads like a table instead of a plain list, matching how
        // OnQueueDrawItem lays each row out below it.
        var tableHeader = new Panel { Dock = DockStyle.Top, Height = 24 };
        var headerName = new Label
        {
            Text = "TAQDIMOTCHI",
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = AppColors.TextSecondary,
            Font = QueueHeaderFont
        };
        tableHeader.Controls.Add(headerName);

        var tableHeaderRule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = AppColors.Border, Margin = new Padding(0, 0, 0, 4) };

        var selectHint = new Label
        {
            Text = "2 marta bosing - joriy/qayta tanlash",
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = AppColors.TextSecondary,
            Font = new Font("Segoe UI", 8f, FontStyle.Italic)
        };

        _queueListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.PanelAlt,
            ForeColor = AppColors.TextPrimary,
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

        panel.Controls.Add(_queueListBox);
        panel.Controls.Add(selectHint);
        panel.Controls.Add(tableHeaderRule);
        panel.Controls.Add(tableHeader);
        panel.Controls.Add(toolbarWrap);
        panel.Controls.Add(searchWrap);
        panel.Controls.Add(header);
        return panel;
    }

    // Filled, rounded buttons (same family as the "BOSHLASH" start button) so add/edit/delete read as
    // clearly-clickable actions instead of the low-contrast bordered-only buttons this used to be.
    private static RoundedButton ToolbarButton(string text, Color accent)
    {
        return new RoundedButton
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 0, 4, 0),
            BackColor = accent,
            CornerRadius = 10,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
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
            ? Blend(AppColors.PanelAlt, AppColors.Accent, 0.30f)
            : e.Index % 2 == 0
                ? AppColors.PanelAlt
                : Blend(AppColors.PanelAlt, AppColors.Panel, 0.5f);
        using (var bgBrush = new SolidBrush(rowColor))
        {
            g.FillRectangle(bgBrush, bounds);
        }

        if (isCurrent)
        {
            using var barBrush = new SolidBrush(AppColors.Success);
            g.FillRectangle(barBrush, bounds.X, bounds.Y, 4, bounds.Height);
        }
        else if (isSelected)
        {
            using var barBrush = new SolidBrush(AppColors.Accent);
            g.FillRectangle(barBrush, bounds.X, bounds.Y, 4, bounds.Height);
        }

        var textLeft = bounds.X + 16;
        var orderRect = new Rectangle(textLeft, bounds.Y, 30, bounds.Height);
        TextRenderer.DrawText(g, $"{p.OrderNumber + 1}.", QueueMetaFont, orderRect, AppColors.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var nameRect = new Rectangle(textLeft + 28, bounds.Y + 7, bounds.Right - (textLeft + 28) - 16, 24);
        TextRenderer.DrawText(g, p.FullName, QueueNameFont, nameRect, AppColors.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var titleRect = new Rectangle(textLeft + 28, bounds.Y + 33, bounds.Width - (textLeft + 28) - 12, 20);
        TextRenderer.DrawText(g, p.Title, QueueTitleFont, titleRect, AppColors.TextSecondary,
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

    private async void OnAddClick()
    {
        using var dialog = new PresentationEditForm("Taqdimot qo'shish", requireFile: true);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _queueService.AddAsync(
                dialog.FullName, dialog.Title,
                dialog.SelectedFilePath!, dialog.SelectedFileType!.Value,
                dialog.PresentationTimeSeconds, dialog.DiscussionTimeSeconds);
            await _session.ReloadQueueAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Qo'shishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnEditClick()
    {
        var selected = GetSelectedPresentation();
        if (selected is null)
        {
            return;
        }

        using var dialog = new PresentationEditForm("Taqdimotni tahrirlash", requireFile: false);
        dialog.LoadFrom(selected);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _queueService.UpdateAsync(
                selected.Id, dialog.FullName, dialog.Title,
                dialog.PresentationTimeSeconds, dialog.DiscussionTimeSeconds,
                dialog.SelectedFilePath, dialog.SelectedFileType);
            await _session.ReloadQueueAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Tahrirlashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnDeleteClick()
    {
        var selected = GetSelectedPresentation();
        if (selected is null)
        {
            return;
        }

        var confirm = MessageBox.Show(this, $"'{selected.FullName} - {selected.Title}' o'chirilsinmi?", "O'chirishni tasdiqlash",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _queueService.DeleteAsync(selected.Id);
            await _session.ReloadQueueAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "O'chirishda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Control BuildCenterPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppColors.Background, Padding = new Padding(20) };

        var card = new Panel { Dock = DockStyle.Fill, BackColor = AppColors.Panel, Padding = new Padding(24) };

        _currentNameLabel = new Label { Dock = DockStyle.Top, Height = 46, Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = AppColors.TextPrimary };
        _currentTitleLabel = new Label { Dock = DockStyle.Top, Height = 32, Font = new Font("Segoe UI", 13, FontStyle.Italic), ForeColor = AppColors.Accent };
        _currentStatusLabel = new Label { Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = AppColors.Warning };

        _startButton = new RoundedButton
        {
            Text = "BOSHLASH",
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = AppColors.Success,
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        };
        _startButton.Click += (_, _) => OnStartClick();

        var hint = new Label
        {
            Text = "Boshlangach, fayl Namoyish ekranida ochiladi - taymer va Keyingi tugmasi ham o'sha yerda.",
            Dock = DockStyle.Bottom,
            Height = 24,
            ForeColor = AppColors.TextSecondary,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic)
        };

        // Framed the same way the queue/logs panels frame their own content (PanelAlt fill, small gap from
        // the labels above and the hint/button below) so the preview reads as its own "slide" rather than a
        // random image floating on the card's background.
        var previewWrap = new Panel { Dock = DockStyle.Fill, BackColor = AppColors.Panel, Padding = new Padding(0, 10, 0, 10) };
        _previewBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.PanelAlt,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle
        };
        previewWrap.Controls.Add(_previewBox);

        // Added first: Dock layout claims space in reverse of Add order, so this Fill panel (added first,
        // laid out last) only ever gets whatever's left after the Top labels and Bottom hint/button above
        // and below it have already taken their strips — see BuildLeftQueuePanel's _queueListBox for the
        // same established pattern in this file.
        card.Controls.Add(previewWrap);
        card.Controls.Add(hint);
        card.Controls.Add(_startButton);
        card.Controls.Add(_currentStatusLabel);
        card.Controls.Add(_currentTitleLabel);
        card.Controls.Add(_currentNameLabel);

        panel.Controls.Add(card);
        return panel;
    }

    private Control BuildLogsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppColors.Panel, Padding = new Padding(10) };

        var header = new Label
        {
            Text = "JURNAL",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = AppColors.TextSecondary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        _logsListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.PanelAlt,
            ForeColor = AppColors.TextSecondary,
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
        // A selected row that differs from whatever's actively running is always startable (that's the
        // whole point — jump to any file on demand); with nothing selected, fall back to the original
        // "only when idle" rule so the button doesn't sit falsely enabled mid-presentation.
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
            ? selected.Id != current?.Id || _session.Status is PresentationStatus.Waiting or PresentationStatus.Ready
            : current is not null && _session.Status is PresentationStatus.Waiting or PresentationStatus.Ready;

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

        var absolutePath = _fileStorageService.GetAbsolutePath(target.FilePath);
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
    /// back to Ready, which is what makes starting an old/already-finished file work.</summary>
    private async void OnStartClick()
    {
        try
        {
            var target = GetSelectedPresentation() ?? _session.CurrentPresentation;
            if (target is null)
            {
                return;
            }

            if (_session.CurrentPresentation?.Id != target.Id)
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
