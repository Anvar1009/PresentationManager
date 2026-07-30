using PresentationManager.Domain.Enums;

namespace PresentationManager.Domain.Entities;

public class Presentation
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public required string FullName { get; set; }

    public required string Title { get; set; }

    public required string FilePath { get; set; }

    public PresentationFileType FileType { get; set; }

    public int OrderNumber { get; set; }

    public int PresentationTimeSeconds { get; set; } = 180;

    public int DiscussionTimeSeconds { get; set; } = 120;

    public PresentationStatus Status { get; set; } = PresentationStatus.Waiting;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
