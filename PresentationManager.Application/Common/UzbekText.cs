using PresentationManager.Domain.Enums;

namespace PresentationManager.Application.Common;

/// <summary>Central place for the handful of enum-to-display-text mappings shared between the desktop app
/// (AdminForm/AdminPanelForm/PresentationForm) and the Telegram bot's admin reporting flow, so every surface
/// describes the same status the same way.</summary>
public static class UzbekText
{
    /// <summary>Short status label used in the admin queue list and current-presentation panel.</summary>
    public static string StatusLabel(PresentationStatus status) => status switch
    {
        PresentationStatus.Waiting => "Kutilmoqda",
        PresentationStatus.Ready => "Tayyor",
        PresentationStatus.Running => "Taqdimot ketmoqda",
        PresentationStatus.Paused => "Taqdimot to'xtatildi",
        PresentationStatus.Discussion => "Muhokama",
        PresentationStatus.DiscussionPaused => "Muhokama to'xtatildi",
        PresentationStatus.DiscussionReady => "Muhokama — boshlanishi kutilmoqda",
        PresentationStatus.ExtraDiscussionReady => "Qo'shimcha muhokama — boshlanishi kutilmoqda",
        PresentationStatus.ExtraDiscussion => "Qo'shimcha muhokama",
        PresentationStatus.Finished => "Yakunlandi",
        PresentationStatus.Skipped => "O'tkazib yuborildi",
        _ => status.ToString()
    };

    /// <summary>Friendly role label shown next to the logged-in account (desktop login menu, SuperAdmin's
    /// Foydalanuvchilar grid) instead of the raw <see cref="UserRole"/> enum name.</summary>
    public static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Operator => "Operator",
        UserRole.Admin => "Administrator",
        UserRole.SuperAdmin => "Superadministrator",
        _ => role.ToString()
    };
}
