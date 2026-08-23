namespace PresentationManager.TelegramBot;

public enum SessionStep
{
    AwaitingRegistrationFullName,
    AwaitingRegistrationContact
}

/// <summary>Per-chat conversation progress for the presenter's one-time registration (full name -> shared
/// contact) - Telegram gives no built-in conversation state, so this is tracked in memory for the lifetime of
/// the app. Everything past registration - picking a project, typing a title, uploading the file - now
/// happens entirely on the Telegram Mini App page (see <c>PresentationBotHostedService.SendUploadWebAppButtonAsync</c>
/// and PresentationManager.API's Controllers.Web.PresenterController), so no further in-chat state is needed
/// once a presenter is registered. Judges/linked Admins have their own session types
/// (no in-chat flow at all for judges - they score from the Judge web platform instead).</summary>
public sealed class ChatSession
{
    public SessionStep Step { get; set; } = SessionStep.AwaitingRegistrationFullName;

    public string FullName { get; set; } = string.Empty;
}
