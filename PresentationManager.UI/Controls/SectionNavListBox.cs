using System.Drawing.Drawing2D;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Controls;

/// <summary>Icon+label owner-drawn ListBox for a form's left-hand section navigation (used by
/// SuperAdminPanelForm and AdminPanelForm) - a rounded accent tint and left bar when selected, a subtle
/// hover tint otherwise, matching the app's dark, card-based look instead of a plain default-styled
/// ListBox. Items are set via <see cref="SetSections"/>; the selected one is read back with
/// <see cref="SelectedSection"/>.</summary>
public sealed class SectionNavListBox : ListBox
{
    private int _hoveredIndex = -1;

    public SectionNavListBox()
    {
        Dock = DockStyle.Fill;
        BackColor = LightColors.Panel;
        ForeColor = LightColors.TextPrimary;
        BorderStyle = BorderStyle.None;
        Font = new Font("Segoe UI", 10.5f);
        IntegralHeight = false;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 46;
        Cursor = Cursors.Hand;

        DrawItem += OnDrawItem;
        MouseMove += (_, e) => SetHoveredIndex(IndexFromPoint(e.Location));
        MouseLeave += (_, _) => SetHoveredIndex(-1);
    }

    public void SetSections(IEnumerable<(string Icon, string Label)> sections)
    {
        Items.Clear();
        Items.AddRange(sections.Select(s => (object)s).ToArray());
    }

    public (string Icon, string Label)? SelectedSection => SelectedItem as (string Icon, string Label)?;

    /// <summary>ListBox never actually reports <see cref="DrawItemState.HotLight"/> for its own items (that
    /// flag is a menu/toolstrip thing) - hover has to be tracked by hand from MouseMove.</summary>
    private void SetHoveredIndex(int index)
    {
        if (index == _hoveredIndex)
        {
            return;
        }

        var previous = _hoveredIndex;
        _hoveredIndex = index;
        if (previous >= 0 && previous < Items.Count)
        {
            Invalidate(GetItemRectangle(previous));
        }

        if (_hoveredIndex >= 0 && _hoveredIndex < Items.Count)
        {
            Invalidate(GetItemRectangle(_hoveredIndex));
        }
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not (string icon, string label))
        {
            e.DrawBackground();
            return;
        }

        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var isHot = e.Index == _hoveredIndex;
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
        var labelFont = isSelected ? new Font(Font, FontStyle.Bold) : Font;
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
}
