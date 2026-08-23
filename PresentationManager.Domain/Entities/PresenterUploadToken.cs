namespace PresentationManager.Domain.Entities;

/// <summary>A single-use, short-lived link handed to a presenter from inside the Telegram chat (see
/// <c>PresentationBotHostedService</c>'s "AwaitingFile" step) that opens a Telegram Mini App page
/// (<c>PresentationManager.API.Controllers.Web.PresenterController</c>) to upload the actual file over a
/// plain HTTPS POST instead of through the Telegram Bot API - which caps a bot's own file downloads at 20MB
/// (see <c>PresentationBotHostedService.HandleDocumentAsync</c>'s doc comment). The token itself IS the
/// credential (no login exists for presenters), so it carries everything the bot conversation already
/// gathered (project, title, whether this replaces an existing submission) rather than needing the web page
/// to ask again or share any in-memory session state with the separate BotService process - both processes
/// only ever share the database.</summary>
public class PresenterUploadToken
{
    public int Id { get; set; }

    public required string Token { get; set; }

    /// <summary>Where the post-upload confirmation (identical wording to the in-chat flow's own) gets sent.</summary>
    public long ChatId { get; set; }

    public int ProjectId { get; set; }

    public required string ProjectName { get; set; }

    /// <summary>Null only if a presenter's bot registration was somehow missing when the token was minted -
    /// see <see cref="Presenter"/>; matches <see cref="Presentation.PresenterId"/>'s own nullability.</summary>
    public int? PresenterId { get; set; }

    public required string FullName { get; set; }

    public required string Title { get; set; }

    /// <summary>Set when this upload replaces an existing submission - mirrors
    /// <c>ChatSession.ExistingPresentationId</c>, consumed the same way by
    /// <c>PresentationQueueService.UpdateAsync</c> vs <c>AddAsync</c>.</summary>
    public int? ExistingPresentationId { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set once the upload succeeds - a used-but-not-yet-expired token must not be replayed.</summary>
    public DateTime? UsedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
