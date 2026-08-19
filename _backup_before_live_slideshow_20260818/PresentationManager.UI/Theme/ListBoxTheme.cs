namespace PresentationManager.UI.Theme;

/// <summary>Owner-draws a plain string <see cref="ListBox"/> with a faint divider line under each row and a
/// light accent tint on the selected one — used wherever a list would otherwise look like an undifferentiated
/// block of text (e.g. <c>CriteriaManagementForm</c>/<c>JudgeManagementForm</c>'s lists).</summary>
public static class ListBoxTheme
{
    public static void ApplyRowDividers(ListBox listBox, int itemHeight = 36)
    {
        listBox.DrawMode = DrawMode.OwnerDrawFixed;
        listBox.ItemHeight = itemHeight;
        listBox.DrawItem += (_, e) => DrawItem(listBox, e);
    }

    private static void DrawItem(ListBox listBox, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= listBox.Items.Count)
        {
            e.DrawBackground();
            return;
        }

        var g = e.Graphics;
        var bounds = e.Bounds;
        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        var backColor = isSelected ? Blend(listBox.BackColor, LightColors.Accent, 0.16f) : listBox.BackColor;
        using (var bgBrush = new SolidBrush(backColor))
        {
            g.FillRectangle(bgBrush, bounds);
        }

        var text = listBox.Items[e.Index]?.ToString() ?? string.Empty;
        var textRect = new Rectangle(bounds.X + 14, bounds.Y, bounds.Width - 28, bounds.Height);
        TextRenderer.DrawText(g, text, listBox.Font, textRect, listBox.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        using var linePen = new Pen(LightColors.Border);
        g.DrawLine(linePen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));
}
