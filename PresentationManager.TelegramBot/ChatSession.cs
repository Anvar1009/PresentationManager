namespace PresentationManager.TelegramBot;

public enum SessionStep
{
    AwaitingRegistrationFullName,
    AwaitingRegistrationContact,
    AwaitingProject,
    AwaitingTitle,
    AwaitingFile
}

/// <summary>Per-chat conversation progress — Telegram gives no built-in conversation state, so each chat's
/// step through (one-time) registration and, per upload, project -> title -> file is tracked here in memory
/// for the lifetime of the app. A returning, already-registered presenter skips straight to
/// <see cref="SessionStep.AwaitingProject"/> - see <see cref="PresentationBotHostedService"/>.</summary>
public sealed class ChatSession
{
    public SessionStep Step { get; set; } = SessionStep.AwaitingProject;

    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
