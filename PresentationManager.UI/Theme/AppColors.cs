namespace PresentationManager.UI.Theme;

/// <summary>Shared dark-theme palette so both AdminForm and PresentationForm stay visually consistent.</summary>
public static class AppColors
{
    public static readonly Color Background = Color.FromArgb(24, 24, 32);
    public static readonly Color Panel = Color.FromArgb(34, 34, 46);
    public static readonly Color PanelAlt = Color.FromArgb(42, 42, 56);
    public static readonly Color Border = Color.FromArgb(58, 58, 74);
    public static readonly Color TextPrimary = Color.FromArgb(230, 230, 236);
    public static readonly Color TextSecondary = Color.FromArgb(160, 160, 176);

    // Brighter, more saturated action colors — these are painted as the FULL button fill (not just a
    // border), so they need to read clearly against the dark panel background.
    public static readonly Color Accent = Color.FromArgb(96, 165, 250);
    public static readonly Color Success = Color.FromArgb(52, 211, 153);
    public static readonly Color Warning = Color.FromArgb(251, 191, 36);
    public static readonly Color Danger = Color.FromArgb(248, 81, 73);
    public static readonly Color Neutral = Color.FromArgb(148, 163, 184);

    // Same hue as Warning, but kept as its own constant: PresentationForm's "Muhokamaga o'tish" button uses
    // this for its outline/fill regardless of the timer's low-time warning state, so the two shouldn't be
    // tied to the same semantic meaning even though they currently share a value.
    public static readonly Color DiscussionAction = Color.FromArgb(251, 191, 36);

    // Final-countdown blink pair (last 10 seconds) — distinct from Warning/Danger above, which are static
    // "getting low" tiers at 60s/30s; these two alternate every second to read as urgent rather than just
    // low, so they're brighter/more saturated than the softer Warning/Danger shades.
    public static readonly Color TimerWarningGold = Color.FromArgb(0xFF, 0xC5, 0x2D);
    public static readonly Color TimerCriticalRed = Color.FromArgb(0xFF, 0x4D, 0x4D);
}
