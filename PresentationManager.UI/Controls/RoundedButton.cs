using System.Drawing.Drawing2D;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Controls;

/// <summary>
/// A flat button whose entire face is filled with <see cref="Control.BackColor"/> and drawn with rounded
/// corners plus a thin 1px border. WinForms' stock <c>FlatStyle.Flat</c> only lets you color the border,
/// not fill a rounded shape, so this paints itself manually (anti-aliased) instead.
/// </summary>
public sealed class RoundedButton : Button
{
    /// <summary>Corner radius in pixels at 96 DPI; scaled by <see cref="Control.DeviceDpi"/> at paint time
    /// so corners stay visually proportional (not just larger/blurrier) on high-DPI monitors.</summary>
    public int CornerRadius { get; set; } = 11;

    private bool _hovering;
    private bool _pressed;

    public RoundedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TextAlign = ContentAlignment.MiddleCenter;
        ForeColor = AppColors.Background;
        Font = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point);
        Cursor = Cursors.Hand;
        Padding = new Padding(20, 10, 20, 10);
    }

    /// <summary>Sizes the button to fit its current Text/Font plus <see cref="Control.Padding"/>, clamped to
    /// the 44–48px (at 96 DPI) height band a button this prominent should read at. Call after Text/Font are
    /// final — e.g. once, right after construction — rather than hand-picking pixel dimensions that would
    /// only be right for one particular DPI.</summary>
    public void AutoFitToText()
    {
        var scale = DeviceDpi / 96f;
        var textSize = TextRenderer.MeasureText(Text, Font);
        var width = textSize.Width + (int)Math.Round(Padding.Horizontal * scale);
        var minHeight = (int)Math.Round(44 * scale);
        var maxHeight = (int)Math.Round(48 * scale);
        var height = Math.Clamp(textSize.Height + (int)Math.Round(Padding.Vertical * scale), minHeight, maxHeight);
        Size = new Size(width, height);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovering = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovering = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var scale = DeviceDpi / 96f;

        // Clips the control's own window to the rounded silhouette instead of its full rectangular bounds,
        // so the corners genuinely aren't part of this window at all — whatever really sits behind (a solid
        // panel, or live slide content on the presentation screen) shows through natively there instead of a
        // background-color patch that can never match every possible backdrop this button floats over.
        UpdateRegionToButtonShape(scale);

        var borderWidth = Math.Max(1f, scale);
        var inset = borderWidth / 2f;
        var rect = RectangleF.Inflate(new RectangleF(0, 0, Width, Height), -inset, -inset);
        using var path = RoundedRectPath(rect, CornerRadius * scale);

        var fillColor = !Enabled
            ? AppColors.PanelAlt
            : _pressed
                ? ControlPaint.Dark(BackColor, 0.1f)
                : _hovering
                    ? ControlPaint.Light(BackColor, 0.15f)
                    : BackColor;

        using (var fillBrush = new SolidBrush(fillColor))
        {
            g.FillPath(fillBrush, path);
        }

        // A thin, same-hue-but-darker outline reads as a deliberate edge rather than the thick, mismatched
        // border the button used to have — subtle enough not to fight the slide content behind it.
        using (var borderPen = new Pen(ControlPaint.Dark(fillColor, 0.25f), borderWidth))
        {
            g.DrawPath(borderPen, path);
        }

        // Centered against the full ClientRectangle (not the inset stroke rect) so the border width never
        // nudges the text off-center.
        var textColor = Enabled ? ForeColor : AppColors.TextSecondary;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>Shapes the window region to the full (non-inset) rounded rectangle so the 1px stroke drawn
    /// in <see cref="OnPaint"/> — centered on a slightly inset path — never gets clipped at its outer edge.</summary>
    private void UpdateRegionToButtonShape(float scale)
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var outerPath = RoundedRectPath(new RectangleF(0, 0, Width, Height), CornerRadius * scale);
        Region = new Region(outerPath);
    }

    private static GraphicsPath RoundedRectPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();

        // GDI+'s AddArc throws ArgumentException ("Parameter is not valid") when given a zero-size bounding
        // box - CornerRadius = 0 (a plain sharp-cornered rectangle, as used e.g. by SuperAdminPanelForm's
        // refresh button) would otherwise crash every single paint. A plain rectangle is exactly what a
        // zero radius should look like anyway, so it's not just a guard, it's the correct shape too.
        var d = radius * 2;
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
