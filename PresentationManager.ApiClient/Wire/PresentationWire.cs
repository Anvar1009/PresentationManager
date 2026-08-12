using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.ApiClient.Wire;

/// <summary>Mirrors PresentationManager.API.Dtos.PresentationDto - used for both read responses and
/// write bodies (Add/Update), same reasoning as the server-side type: UpdateAsync always replaces the
/// full entity.</summary>
internal sealed record PresentationWire(
    int Id, int ProjectId, int? PresenterId, string FullName, string Title, string FilePath,
    PresentationFileType FileType, int OrderNumber, int PresentationTimeSeconds, int DiscussionTimeSeconds,
    int ExtraDiscussionTimeSeconds, PresentationStatus Status, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static PresentationWire FromEntity(Presentation p) => new(
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
