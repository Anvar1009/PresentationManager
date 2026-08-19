namespace PresentationManager.UI.Theme;

/// <summary>Light palette for the operator-facing dashboards (Admin/SuperAdmin/Operator panels) — the
/// audience-facing Namoyish Ekrani stays on the dark <see cref="AppColors"/> palette per the project's design
/// spec; this is scoped to the internal control-surface screens rather than a global theme swap.</summary>
public static class LightColors
{
    public static readonly Color Background = Color.White;
    public static readonly Color Panel = Color.White;
    public static readonly Color PanelAlt = Color.FromArgb(244, 246, 249);
    public static readonly Color Border = Color.FromArgb(226, 229, 234);
    public static readonly Color TextPrimary = Color.FromArgb(26, 29, 35);
    public static readonly Color TextSecondary = Color.FromArgb(107, 114, 128);

    public static readonly Color Accent = Color.FromArgb(47, 111, 237);
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    public static readonly Color Warning = Color.FromArgb(217, 119, 6);

    // Same hue as Warning, kept as its own constant to mirror AppColors.DiscussionAction — painted as a full
    // button fill (not just a border/text color), so it needs its own slightly deeper shade to stay readable
    // with white button text on the light background.
    public static readonly Color DiscussionAction = Color.FromArgb(180, 95, 6);
}
