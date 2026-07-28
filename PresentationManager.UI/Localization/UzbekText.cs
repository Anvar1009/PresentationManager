using PresentationManager.Domain.Enums;

namespace PresentationManager.UI.Localization;

/// <summary>Central place for the handful of enum-to-display-text mappings shared between AdminForm and
/// PresentationForm, so both screens describe the same status the same way.</summary>
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
        PresentationStatus.Finished => "Yakunlandi",
        PresentationStatus.Skipped => "O'tkazib yuborildi",
        _ => status.ToString()
    };
}
