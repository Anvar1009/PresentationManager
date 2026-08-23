namespace PresentationManager.Domain.Entities;

/// <summary>A short-lived link handed to a presenter from inside the Telegram chat (see
/// <c>PresentationBotHostedService.SendUploadWebAppButtonAsync</c>) that opens a Telegram Mini App page
/// (<c>PresentationManager.API.Controllers.Web.PresenterController</c>) - the ONLY way a presenter submits a
/// presentation now: picking the project, typing the title, and uploading the file all happen there, over a
/// plain HTTPS POST instead of through the Telegram Bot API, whose own file downloads are capped at 20MB. The
/// token itself IS the credential (no login exists for presenters) - it only has to identify WHO is uploading
/// (this chat's already-registered <see cref="Presenter"/>), not WHAT, since the project/title are chosen on
/// the page itself. Valid for its whole lifetime, not single-use: a presenter may legitimately submit to more
/// than one assigned project, or fix a mistake and resubmit, within the same link's window.</summary>
public class PresenterUploadToken
{
    public int Id { get; set; }

    public required string Token { get; set; }

    /// <summary>Where the post-upload confirmation (identical wording to the old in-chat flow's own) gets sent.</summary>
    public long ChatId { get; set; }

    public int PresenterId { get; set; }

    public required string FullName { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
