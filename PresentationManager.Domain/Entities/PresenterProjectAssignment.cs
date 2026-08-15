namespace PresentationManager.Domain.Entities;

/// <summary>Admin's confirmation that a bot-registered <see cref="Presenter"/> may take part in a specific
/// <see cref="Project"/> — the row's mere existence IS the approval (no separate status flag), created once
/// from the Admin panel and immediately unlocking that presenter's upload flow in the Telegram bot (see
/// <c>PresentationBotHostedService.ShowProjectListAsync</c>).</summary>
public class PresenterProjectAssignment
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public int PresenterId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
