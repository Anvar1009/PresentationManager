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
    public TransparentOverlayLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(ForeColor);
        e.Graphics.DrawString(Text, Font, brush, ClientRectangle, StringFormat.GenericDefault);

        UpdateRegionToGlyphs();
    }

    /// <summary>Reshapes the window region to hug just the rendered text every repaint — cheap enough here
    /// since this label only ever shows a handful of digits and repaints at most once a second.</summary>
    private void UpdateRegionToGlyphs()
    {
        if (string.IsNullOrEmpty(Text) || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        using var g = CreateGraphics();
        var emSizeInPixels = Font.Size * g.DpiY / 72f;

        using var path = new GraphicsPath();
        path.AddString(Text, Font.FontFamily, (int)Font.Style, emSizeInPixels, ClientRectangle, StringFormat.GenericDefault);
        Region = new Region(path);
    }
}
