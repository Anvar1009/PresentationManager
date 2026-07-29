using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PresentationManager.UI.Controls;

/// <summary>
/// A Label whose background is genuinely see-through to whatever sits behind it, including windowed,
/// hardware-composited controls like WebView2 — plain WinForms "transparent" BackColor can't reach those
/// since it only repaints the logical parent's own background, not a sibling's live composited surface.
///
/// Two native-window tricks were tried before this and both failed in practice: WS_EX_TRANSPARENT (WebView2
/// draws via DirectComposition, which bypasses the GDI paint-passthrough that style relies on), and a
/// WS_EX_LAYERED color-key overlay (CreateWindowEx for a layered child window failed outright in this
/// environment — "Error creating window handle" the moment the presentation screen tried to open). Both
/// depend on window styles that aren't reliably available everywhere.
///
/// This instead shapes the control's own <see cref="Control.Region"/> to exactly the outline of the
/// rendered digits (via <see cref="GraphicsPath.AddString"/>) — pixels outside that shape simply aren't
/// part of the window at all, so whatever is really behind it on screen shows through with no special
/// window style required, only the ordinary <c>SetWindowRgn</c> plumbing already built into WinForms'
/// <see cref="Control.Region"/> property.
/// </summary>
public sealed class TransparentOverlayLabel : Label
{
    /// <summary>When set, a stroked outline in this color is drawn behind the fill — needed because this
    /// label can end up floating over arbitrary, unpredictable slide content (a light PDF background can
    /// swallow a plain-filled light/mid-tone <see cref="Control.ForeColor"/> like <c>AppColors.DiscussionAction</c>
    /// with no contrast at all), unlike the fixed dark app background everywhere else this control is used.
    /// Ignored when <see cref="PillColor"/> is set — a solid pill already guarantees contrast on its own.</summary>
    public Color? OutlineColor { get; set; }

    /// <summary>When set, the label paints itself as a solid capsule ("chip/badge") in this color instead of
    /// hugging just the glyph outlines — <see cref="Control.Padding"/> controls the breathing room between the
    /// text and the capsule edge. Unlike <see cref="OutlineColor"/>, this reads as a deliberate UI element (a
    /// tag) rather than bare floating text, and guarantees legibility regardless of what slide is behind it
    /// since the whole capsule becomes part of the control's own opaque window region.</summary>
    public Color? PillColor { get; set; }

    /// <summary>Optional thin stroke around the <see cref="PillColor"/> capsule for a bit of definition
    /// against dark slide backgrounds close in value to the pill fill itself. No effect without PillColor.</summary>
    public Color? PillBorderColor { get; set; }

    public TransparentOverlayLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        if (PillColor is { } pill)
        {
            using var pillPath = CreateCapsulePath(ClientRectangle);
            using var pillBrush = new SolidBrush(pill);
            e.Graphics.FillPath(pillBrush, pillPath);

            if (PillBorderColor is { } pillBorder)
            {
                using var pillPen = new Pen(pillBorder, 1.5f);
                e.Graphics.DrawPath(pillPen, pillPath);
            }
        }
        else if (OutlineColor is { } outline)
        {
            var emSizeInPixels = Font.Size * e.Graphics.DpiY / 72f;
            using var outlinePath = new GraphicsPath();
            outlinePath.AddString(Text, Font.FontFamily, (int)Font.Style, emSizeInPixels, ClientRectangle, StringFormat.GenericDefault);
            using var pen = new Pen(outline, 3f) { LineJoin = LineJoin.Round };
            e.Graphics.DrawPath(pen, outlinePath);
        }

        var textBounds = new Rectangle(
            Padding.Left, Padding.Top,
            Math.Max(0, ClientSize.Width - Padding.Horizontal),
            Math.Max(0, ClientSize.Height - Padding.Vertical));
        var format = PillColor is not null
            ? new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }
            : StringFormat.GenericDefault;

        using var brush = new SolidBrush(ForeColor);
        e.Graphics.DrawString(Text, Font, brush, textBounds, format);

        UpdateRegionToGlyphs();
    }

    /// <summary>Reshapes the window region every repaint — cheap enough here since this label only ever shows
    /// a short line of text/digits and repaints at most once a second. With <see cref="PillColor"/> set, the
    /// region is simply the capsule itself (a deliberate solid tag, not a see-through cutout). Otherwise it
    /// hugs just the rendered glyphs, widened to also cover the stroked <see cref="OutlineColor"/> band around
    /// each one when set (just the glyph fill area would otherwise clip the outside half of that stroke, the
    /// same way <see cref="OnPaint"/> draws it).</summary>
    private void UpdateRegionToGlyphs()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        if (PillColor is not null)
        {
            Region = new Region(CreateCapsulePath(ClientRectangle));
            return;
        }

        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        using var g = CreateGraphics();
        var emSizeInPixels = Font.Size * g.DpiY / 72f;

        using var path = new GraphicsPath();
        path.AddString(Text, Font.FontFamily, (int)Font.Style, emSizeInPixels, ClientRectangle, StringFormat.GenericDefault);

        var region = new Region(path);
        if (OutlineColor is { } outline)
        {
            using var strokePath = (GraphicsPath)path.Clone();
            using var pen = new Pen(outline, 3f) { LineJoin = LineJoin.Round };
            strokePath.Widen(pen);
            region.Union(strokePath);
        }

        Region = region;
    }

    /// <summary>Fully-rounded "pill" rectangle (corner radius = half the height) used for <see cref="PillColor"/>.</summary>
    private static GraphicsPath CreateCapsulePath(Rectangle bounds)
    {
        var diameter = Math.Min(bounds.Height, bounds.Width);
        var radius = diameter / 2f;
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
}
