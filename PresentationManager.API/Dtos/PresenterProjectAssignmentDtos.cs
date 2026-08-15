using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Dtos;

public sealed record PresenterProjectAssignmentDto(int Id, int ProjectId, int PresenterId, DateTime CreatedAt)
{
    public static PresenterProjectAssignmentDto FromEntity(PresenterProjectAssignment a) =>
        new(a.Id, a.ProjectId, a.PresenterId, a.CreatedAt);

    public PresenterProjectAssignment ToEntity() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        PresenterId = PresenterId,
        CreatedAt = CreatedAt
    };
}
