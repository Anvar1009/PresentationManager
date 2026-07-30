using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PresentationManager.UI.Controls;

/// <summary>
/// Fluent-style outline button: white/transparent fill, a thin colored border, a procedurally-drawn
/// refresh glyph (no icon font, no bitmap asset - just <see cref="Graphics"/>), and Windows 11-style
/// hover/press/disabled states. Fully custom-painted - the base <see cref="Button"/> never draws anything
/// of its own.
/// </summary>
public class ModernOutlineButton : Button
{
    public int BorderThickness { get; set; } = 1;

    public int BorderRadius { get; set; } = 8;

    public Color BorderColor { get; set; } = ColorTranslator.FromHtml("#3B82F6");

    public Color TextColor { get; set; } = ColorTranslator.FromHtml("#2563EB");

    public Color HoverBackColor { get; set; } = ColorTranslator.FromHtml("#EFF6FF");

    public Color HoverBorderColor { get; set; } = ColorTranslator.FromHtml("#2563EB");

    public Color PressedBackColor { get; set; } = ColorTranslator.FromHtml("#DBEAFE");

    public Color PressedBorderColor { get; set; } = ColorTranslator.FromHtml("#1D4ED8");

    public Color DisabledBorderColor { get; set; } = ColorTranslator.FromHtml("#D1D5DB");

    public Color DisabledTextColor { get; set; } = ColorTranslator.FromHtml("#9CA3AF");

    /// <summary>Set false to render as a text-only outline button (the refresh glyph is otherwise always
    /// drawn - this control isn't a generic icon slot, it's specifically the refresh button style).</summary>
    public bool ShowIcon { get; set; } = true;

    public Size IconSize { get; set; } = new(16, 16);

    public int IconTextGap { get; set; } = 8;

    private bool _isHovering;
    private bool _isPressed;

    public ModernOutlineButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = TextColor;
        Font = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point);
        Cursor = Cursors.Hand;
        Padding = new Padding(18, 10, 18, 10);
        Size = new Size(145, 40);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _isHovering = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _isHovering = false;
        _isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _isPressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _isHovering = false;
        _isPressed = false;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    /// <summary>Left empty - this control owns its entire surface from <see cref="OnPaint"/>; painting the
    /// background here too would just be redundant work repeated every frame.</summary>
    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var (backColor, borderColor, contentColor) = GetStateColors();
        var half = BorderThickness / 2f;
        var bounds = new RectangleF(half, half, Width - BorderThickness - 1, Height - BorderThickness - 1);

        if (Enabled)
        {
            DrawShadow(g, bounds);
        }

        // The "pressed down" feel is just the face (fill/border/icon/text) nudged 1px down within its own
        // fixed footprint - the shadow above stays put, which is what sells the illusion of the button
        // actually depressing rather than the whole control silently moving.
        if (_isPressed)
        {
            g.TranslateTransform(0, 1);
        }

        using (var path = RoundedRectPath(bounds, BorderRadius))
        {
            using (var bgBrush = new SolidBrush(backColor))
            {
                g.FillPath(bgBrush, path);
            }

            if (BorderThickness > 0)
            {
                using var borderPen = new Pen(borderColor, BorderThickness);
                g.DrawPath(borderPen, path);
            }
        }

        DrawContent(g, contentColor);

        if (_isPressed)
        {
            g.ResetTransform();
        }
    }

    private (Color Back, Color Border, Color Content) GetStateColors()
    {
        if (!Enabled)
        {
            return (Color.Transparent, DisabledBorderColor, DisabledTextColor);
        }

        if (_isPressed)
        {
            return (PressedBackColor, PressedBorderColor, TextColor);
        }

        if (_isHovering)
        {
            return (HoverBackColor, HoverBorderColor, TextColor);
        }

        return (Color.Transparent, BorderColor, TextColor);
    }

    /// <summary>A soft, barely-there elevation cue (a few translucent rounded rects stacked with increasing
    /// offset/decreasing opacity) rather than a single hard-edged copy - GDI+ has no native blur, so this is
    /// the standard cheap approximation of one.</summary>
    private void DrawShadow(Graphics g, RectangleF bounds)
    {
        for (var i = 3; i >= 1; i--)
        {
            var shadowBounds = new RectangleF(bounds.X, bounds.Y + i * 0.6f, bounds.Width, bounds.Height);
            using var shadowPath = RoundedRectPath(shadowBounds, BorderRadius);
            using var shadowBrush = new SolidBrush(Color.FromArgb(6 * i, 15, 23, 42));
            g.FillPath(shadowBrush, shadowPath);
        }
    }

    private void DrawContent(Graphics g, Color contentColor)
    {
        var contentArea = new RectangleF(
            Padding.Left, Padding.Top,
            Width - Padding.Horizontal, Height - Padding.Vertical);

        var textSize = string.IsNullOrEmpty(Text) ? SizeF.Empty : g.MeasureString(Text, Font);
        var hasIcon = ShowIcon;
        var gap = hasIcon && textSize.Width > 0 ? IconTextGap : 0;
        var totalWidth = (hasIcon ? IconSize.Width : 0) + gap + textSize.Width;

        var x = contentArea.X + Math.Max(0, (contentArea.Width - totalWidth) / 2f);
        var centerY = contentArea.Y + contentArea.Height / 2f;

        if (hasIcon)
        {
            var iconRect = new RectangleF(x, centerY - IconSize.Height / 2f, IconSize.Width, IconSize.Height);
            DrawRefreshIcon(g, iconRect, contentColor);
            x += IconSize.Width + gap;
        }

        if (textSize.Width > 0)
        {
            using var textBrush = new SolidBrush(contentColor);
            var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            var textRect = new RectangleF(x, centerY - textSize.Height / 2f, textSize.Width, textSize.Height);
            g.DrawString(Text, Font, textBrush, textRect, format);
        }
    }

    /// <summary>Draws a circular-arrow "refresh" glyph by hand: a ~280° arc plus a triangular arrowhead
    /// tangent to its leading end - the standard reload icon shape, without depending on Segoe Fluent Icons
    /// being installed or shipping a bitmap asset.</summary>
    private static void DrawRefreshIcon(Graphics g, RectangleF bounds, Color color)
    {
        const float strokeWidth = 1.6f;
        const float startAngle = -50f;
        const float sweepAngle = 280f;

        var arcRect = RectangleF.Inflate(bounds, -strokeWidth, -strokeWidth);
        using var pen = new Pen(color, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, arcRect, startAngle, sweepAngle);

        var centerX = arcRect.X + arcRect.Width / 2f;
        var centerY = arcRect.Y + arcRect.Height / 2f;
        var radius = arcRect.Width / 2f;

        var endAngleRad = (startAngle + sweepAngle) * MathF.PI / 180f;
        var arcEndX = centerX + radius * MathF.Cos(endAngleRad);
        var arcEndY = centerY + radius * MathF.Sin(endAngleRad);

        // Tangent direction of travel at the arc's leading end (clockwise sweep -> tangent = angle + 90deg).
        var tangentRad = endAngleRad + MathF.PI / 2f;
        var arrowLength = bounds.Width * 0.34f;
        var arrowHalfWidth = bounds.Width * 0.22f;

        var tip = new PointF(
            arcEndX + arrowLength * MathF.Cos(tangentRad),
            arcEndY + arrowLength * MathF.Sin(tangentRad));
        var back1 = new PointF(
            tip.X - arrowLength * MathF.Cos(tangentRad) + arrowHalfWidth * MathF.Cos(tangentRad - MathF.PI / 2f),
            tip.Y - arrowLength * MathF.Sin(tangentRad) + arrowHalfWidth * MathF.Sin(tangentRad - MathF.PI / 2f));
        var back2 = new PointF(
            tip.X - arrowLength * MathF.Cos(tangentRad) + arrowHalfWidth * MathF.Cos(tangentRad + MathF.PI / 2f),
            tip.Y - arrowLength * MathF.Sin(tangentRad) + arrowHalfWidth * MathF.Sin(tangentRad + MathF.PI / 2f));

        using var arrowBrush = new SolidBrush(color);
        g.FillPolygon(arrowBrush, [tip, back1, back2]);
    }

    private static GraphicsPath RoundedRectPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();

        // A radius past half the shorter side (or <= 0) would hand GDI+'s AddArc an oversized/zero-sized
        // bounding box, which throws ArgumentException for either - clamping is both the fix and the
        // visually correct shape (a radius that large is already a full stadium/plain rectangle anyway).
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
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
