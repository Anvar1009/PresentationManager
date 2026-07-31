using System.Drawing.Drawing2D;
using PresentationManager.Application.Common;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.UI.Controls;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Modal "pick what's next" dialog shown from <see cref="PresentationForm"/>'s control bar once a
/// discussion ends — replaces the old behavior of silently auto-starting whatever came next in queue order,
/// letting the operator instead choose the next presenter themselves.</summary>
/// <remarks>Deliberately does none of the actual session work (finish/select/start) itself — those are all
/// real async DB calls, and running them from a button click handler nested inside this dialog's own
/// <c>ShowDialog</c> message loop was unreliable (the presentation would silently fail to start). Instead
/// this dialog only ever records <see cref="SelectedPresentationId"/> and closes; the caller
/// (<see cref="PresentationForm"/>) does the actual work afterward, once back on its own normal message
/// loop.</remarks>
public sealed class PresentationPickerForm : Form
{
    private const int CardCornerRadius = 10;
    private const int RowSpacing = 6;
    private const int BadgeDiameter = 36;
    private const int PillHeight = 24;

    private readonly PresentationSessionController _session;
    private readonly ListBox _listBox;
    private readonly RoundedButton _startButton;
    private readonly Font _badgeFont = new("Segoe UI", 11, FontStyle.Bold);
    private readonly Font _nameFont = new("Segoe UI", 11.5f, FontStyle.Bold);
    private readonly Font _titleFont = new("Segoe UI", 10f);
    private readonly Font _pillFont = new("Segoe UI", 8.5f, FontStyle.Bold);
    private List<Presentation> _items = [];
    private int _hoveredIndex = -1;

    /// <summary>Set once the operator picks a row and confirms — null if the dialog was dismissed without
    /// choosing anything.</summary>
    public int? SelectedPresentationId { get; private set; }

    public PresentationPickerForm(PresentationSessionController session)
    {
        _session = session;

        Text = "Keyingi taqdimotni tanlang";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        // Deliberately CenterScreen, not CenterParent — kept even now that the owner passed to ShowDialog is
        // the full-screen PresentationForm itself, since an earlier owner (a thin ~150px strip pinned to the
        // bottom of the screen) once pushed most of this dialog, including the Dock.Bottom BOSHLASH button,
        // off the bottom edge of the screen entirely, which is exactly why picking something and pressing
        // Boshlash appeared to do nothing: the button wasn't reachable, on-screen, to actually click.
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 560);

        var header = new Label
        {
            Text = "Keyingi taqdimotchini tanlang",
            Dock = DockStyle.Top,
            Height = 32,
            ForeColor = LightColors.TextPrimary,
            Font = new Font("Segoe UI", 16, FontStyle.Bold)
        };

        var subheader = new Label
        {
            // Height includes 20px of blank space below the text itself (rather than relying on Margin,
            // which the plain Dock/Anchor layout engine used here ignores — only FlowLayoutPanel/
            // TableLayoutPanel honor it) to leave section-spacing breathing room before the list card.
            Text = "Ro'yxatdan birini tanlang, so'ng muhokamani yakunlab boshlang.",
            Dock = DockStyle.Top,
            Height = 44,
            ForeColor = LightColors.TextSecondary,
            Font = new Font("Segoe UI", 10)
        };

        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = LightColors.Panel,
            ForeColor = LightColors.TextPrimary,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 64,
            Cursor = Cursors.Hand
        };
        _listBox.SelectedIndexChanged += (_, _) => _startButton.Enabled = _listBox.SelectedIndex >= 0;
        _listBox.DoubleClick += (_, _) => TryAccept();
        _listBox.DrawItem += ListBox_DrawItem;
        _listBox.MouseMove += (_, e) =>
        {
            var index = _listBox.IndexFromPoint(e.Location);
            SetHoveredIndex(index >= 0 && index < _items.Count ? index : -1);
        };
        _listBox.MouseLeave += (_, _) => SetHoveredIndex(-1);

        var listCard = new Panel { Dock = DockStyle.Fill, BackColor = LightColors.Panel };
        listCard.Paint += (_, e) =>
        {
            using var pen = new Pen(LightColors.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, listCard.Width - 1, listCard.Height - 1);
        };
        listCard.Controls.Add(_listBox);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 24, 24, 0) };
        content.Controls.Add(listCard);
        content.Controls.Add(subheader);
        content.Controls.Add(header);

        _startButton = new RoundedButton
        {
            Text = "BOSHLASH",
            Dock = DockStyle.Fill,
            BackColor = LightColors.Success,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            Enabled = false
        };
        _startButton.Click += (_, _) => TryAccept();

        var actionBar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 24) };
        actionBar.Controls.Add(_startButton);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));
        root.Controls.Add(content, 0, 0);
        root.Controls.Add(actionBar, 0, 1);

        Controls.Add(root);

        Load += (_, _) => PopulateList();
        FormClosed += (_, _) =>
        {
            _badgeFont.Dispose();
            _nameFont.Dispose();
            _titleFont.Dispose();
            _pillFont.Dispose();
        };
    }

    private void PopulateList()
    {
        _items = _session.Queue.ToList();
        _listBox.Items.Clear();
        foreach (var p in _items)
        {
            // Custom-painted in ListBox_DrawItem — this text only backs the item count/index and screen readers.
            _listBox.Items.Add($"{p.OrderNumber + 1}. {p.FullName} - {p.Title}");
        }
    }

    private void SetHoveredIndex(int index)
    {
        if (index == _hoveredIndex)
        {
            return;
        }

        var previous = _hoveredIndex;
        _hoveredIndex = index;
        if (previous >= 0 && previous < _listBox.Items.Count)
        {
            _listBox.Invalidate(_listBox.GetItemRectangle(previous));
        }

        if (_hoveredIndex >= 0 && _hoveredIndex < _listBox.Items.Count)
        {
            _listBox.Invalidate(_listBox.GetItemRectangle(_hoveredIndex));
        }
    }

    private void ListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var backBrush = new SolidBrush(LightColors.Panel))
        {
            e.Graphics.FillRectangle(backBrush, e.Bounds);
        }

        if (e.Index < 0 || e.Index >= _items.Count)
        {
            return;
        }

        var presentation = _items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        var hovered = e.Index == _hoveredIndex;

        var rowRect = Rectangle.Inflate(e.Bounds, -12, -RowSpacing / 2);
        // ControlPaint.Light can't lighten LightColors.Panel any further - it's already pure white - so the
        // hover state needs to darken slightly instead, unlike the old dark-theme version of this row.
        var rowColor = selected ? LightColors.PanelAlt : hovered ? ControlPaint.Dark(LightColors.Panel, 0.03f) : LightColors.Panel;

        using (var rowPath = RoundedRect(rowRect, CardCornerRadius))
        using (var rowBrush = new SolidBrush(rowColor))
        {
            e.Graphics.FillPath(rowBrush, rowPath);
        }

        if (selected)
        {
            using var accentBrush = new SolidBrush(LightColors.Accent);
            using var accentBarPath = RoundedRect(new Rectangle(rowRect.X, rowRect.Y, 4, rowRect.Height), 2);
            e.Graphics.FillPath(accentBrush, accentBarPath);
        }

        var badgeRect = new Rectangle(rowRect.X + 16, rowRect.Y + (rowRect.Height - BadgeDiameter) / 2, BadgeDiameter, BadgeDiameter);
        using (var badgeBrush = new SolidBrush(LightColors.Accent))
        {
            e.Graphics.FillEllipse(badgeBrush, badgeRect);
        }

        TextRenderer.DrawText(e.Graphics, (presentation.OrderNumber + 1).ToString(), _badgeFont, badgeRect, LightColors.Background,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var statusText = UzbekText.StatusLabel(presentation.Status).ToUpperInvariant();
        var statusColor = StatusColor(presentation.Status);
        var statusSize = TextRenderer.MeasureText(e.Graphics, statusText, _pillFont);
        var pillWidth = statusSize.Width + 20;
        var pillRect = new Rectangle(rowRect.Right - pillWidth - 16, rowRect.Y + (rowRect.Height - PillHeight) / 2, pillWidth, PillHeight);

        using (var pillBrush = new SolidBrush(Color.FromArgb(46, statusColor)))
        using (var pillPath = RoundedRect(pillRect, PillHeight / 2f))
        {
            e.Graphics.FillPath(pillBrush, pillPath);
        }

        TextRenderer.DrawText(e.Graphics, statusText, _pillFont, pillRect, statusColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var textLeft = badgeRect.Right + 14;
        var textWidth = Math.Max(0, pillRect.Left - textLeft - 12);
        var nameHeight = TextRenderer.MeasureText(presentation.FullName, _nameFont).Height;
        var titleHeight = TextRenderer.MeasureText(presentation.Title, _titleFont).Height;
        var blockTop = rowRect.Y + (rowRect.Height - nameHeight - titleHeight - 2) / 2;

        var nameRect = new Rectangle(textLeft, blockTop, textWidth, nameHeight);
        var titleRect = new Rectangle(textLeft, blockTop + nameHeight + 2, textWidth, titleHeight);

        TextRenderer.DrawText(e.Graphics, presentation.FullName, _nameFont, nameRect, LightColors.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, presentation.Title, _titleFont, titleRect, LightColors.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static Color StatusColor(PresentationStatus status) => status switch
    {
        PresentationStatus.Running => LightColors.Success,
        PresentationStatus.Paused => LightColors.Warning,
        PresentationStatus.Discussion or PresentationStatus.DiscussionReady => LightColors.DiscussionAction,
        PresentationStatus.DiscussionPaused => LightColors.Warning,
        PresentationStatus.Ready => LightColors.Accent,
        PresentationStatus.Finished or PresentationStatus.Skipped => LightColors.TextSecondary,
        _ => LightColors.TextSecondary // Waiting
    };

    private static GraphicsPath RoundedRect(Rectangle bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arcRect = new RectangleF(bounds.X, bounds.Y, diameter, diameter);

        var path = new GraphicsPath();
        path.AddArc(arcRect, 180, 90);
        arcRect.X = bounds.Right - diameter;
        path.AddArc(arcRect, 270, 90);
        arcRect.Y = bounds.Bottom - diameter;
        path.AddArc(arcRect, 0, 90);
        arcRect.X = bounds.X;
        path.AddArc(arcRect, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void TryAccept()
    {
        var index = _listBox.SelectedIndex;
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        SelectedPresentationId = _items[index].Id;

        // Setting DialogResult alone closes a form shown via ShowDialog (see PresentationEditForm's
        // Saqlash button for the same established pattern elsewhere in this app) — an extra explicit
        // Close() call here raced with that and was the actual reason picking something and pressing
        // Boshlash silently did nothing: ShowDialog's caller never reliably saw DialogResult.OK.
        DialogResult = DialogResult.OK;
    }
}
