namespace PresentationManager.Domain.Entities;

/// <summary>A judge assigned to a project — Admin pre-registers the phone number before the judge ever
/// touches the bot; <see cref="TelegramChatId"/> stays null until that phone number shows up in a contact
/// share, at which point the bot links the two (see <c>JudgeService.LinkByPhoneAsync</c>). The same person
/// can judge multiple projects, which is why this is one row per (project, phone) rather than a single
/// global judge identity.</summary>
public class Judge
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public required string PhoneNumber { get; set; }

    public string? FullName { get; set; }

    public long? TelegramChatId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
