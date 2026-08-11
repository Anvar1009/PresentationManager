using PresentationManager.Domain.Enums;

namespace PresentationManager.Domain.Entities;

public class Presentation
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    /// <summary>Set when submitted via the Telegram bot (linked to the registered <see cref="Presenter"/>);
    /// null when the operator adds it manually, since there's no bot registration to link to in that case.</summary>
    public int? PresenterId { get; set; }

    public required string FullName { get; set; }

    public required string Title { get; set; }

    public required string FilePath { get; set; }

    public PresentationFileType FileType { get; set; }

    public int OrderNumber { get; set; }

    public int PresentationTimeSeconds { get; set; } = 180;

    public int DiscussionTimeSeconds { get; set; } = 120;

    /// <summary>Optional extra time offered once <see cref="DiscussionTimeSeconds"/> runs out — 0 means no
    /// extra time is configured, so the discussion phase ends exactly as it did before this field existed.</summary>
    public int ExtraDiscussionTimeSeconds { get; set; } = 0;

    public PresentationStatus Status { get; set; } = PresentationStatus.Waiting;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
