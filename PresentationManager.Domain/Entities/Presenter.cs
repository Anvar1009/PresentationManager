namespace PresentationManager.Domain.Entities;

/// <summary>A presenter registered through the Telegram bot — captured once so future uploads only need to
/// ask for a project and a title, not the presenter's identity every time.</summary>
public class Presenter
{
    public int Id { get; set; }

    public long TelegramChatId { get; set; }

    public required string FullName { get; set; }

    public string? PhoneNumber { get; set; }

    public string? TelegramUsername { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
