using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Dtos;

/// <summary>Mirrors the full <see cref="Presentation"/> entity - used for both read responses and
/// write request bodies (Add/Update), since IPresentationRepository.UpdateAsync replaces every scalar
/// column with whatever the caller passes, so the caller (PresentationQueueService, running client-side
/// in PresentationManager.UI) always sends the complete entity, not a partial patch.</summary>
public sealed record PresentationDto(
    int Id,
    int ProjectId,
    int? PresenterId,
    string FullName,
    string Title,
    string FilePath,
    PresentationFileType FileType,
    int OrderNumber,
    int PresentationTimeSeconds,
    int DiscussionTimeSeconds,
    int ExtraDiscussionTimeSeconds,
    PresentationStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static PresentationDto FromEntity(Presentation p) => new(
        p.Id, p.ProjectId, p.PresenterId, p.FullName, p.Title, p.FilePath, p.FileType, p.OrderNumber,
        p.PresentationTimeSeconds, p.DiscussionTimeSeconds, p.ExtraDiscussionTimeSeconds, p.Status,
        p.CreatedAt, p.UpdatedAt);

    public Presentation ToEntity() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        PresenterId = PresenterId,
        FullName = FullName,
        Title = Title,
        FilePath = FilePath,
        FileType = FileType,
        OrderNumber = OrderNumber,
        PresentationTimeSeconds = PresentationTimeSeconds,
        DiscussionTimeSeconds = DiscussionTimeSeconds,
        ExtraDiscussionTimeSeconds = ExtraDiscussionTimeSeconds,
        Status = Status,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}

public sealed record ReorderPresentationsRequest(List<int> OrderedPresentationIds);
