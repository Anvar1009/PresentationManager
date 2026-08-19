namespace PresentationManager.TelegramBot;

public enum SessionStep
{
    AwaitingRegistrationFullName,
    AwaitingRegistrationContact,
    AwaitingProject,
    AwaitingTitle,
    AwaitingFile
}

/// <summary>Per-chat conversation progress for the presenter upload flow — Telegram gives no built-in
/// conversation state, so each chat's step through (one-time) registration and, per upload, project ->
/// title -> file is tracked here in memory for the lifetime of the app. A returning, already-registered
/// presenter skips straight to <see cref="SessionStep.AwaitingProject"/> - see
/// <see cref="PresentationBotHostedService"/>. Judges no longer have an in-chat flow at all (they score from
/// the Judge web platform instead - see <c>PresentationBotHostedService.ShowJudgeWebRedirectAsync</c>).</summary>
public sealed class ChatSession
{
    public SessionStep Step { get; set; } = SessionStep.AwaitingProject;

    /// <summary>Set once the registered <see cref="PresentationManager.Domain.Entities.Presenter"/> is known
    /// (either just-created or looked up by chat id) - passed through to
    /// <c>PresentationQueueService.AddAsync</c> so the submitted presentation is linked back to it.</summary>
    public int? PresenterId { get; set; }

    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Set (in <c>HandleProjectSelectionCallbackAsync</c>) when this presenter already has a
    /// presentation in the chosen project - the upload that follows then goes through
    /// <c>PresentationQueueService.UpdateAsync</c> against this Id instead of <c>AddAsync</c>, replacing the
    /// existing title/file rather than creating a second, duplicate entry for the same project.</summary>
    public int? ExistingPresentationId { get; set; }
}
