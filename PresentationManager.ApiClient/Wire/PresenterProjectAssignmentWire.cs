using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient.Wire;

/// <summary>Mirrors PresentationManager.API.Dtos.PresenterProjectAssignmentDto.</summary>
internal sealed record PresenterProjectAssignmentWire(int Id, int ProjectId, int PresenterId, DateTime CreatedAt)
{
    public static PresenterProjectAssignmentWire FromEntity(PresenterProjectAssignment a) =>
        new(a.Id, a.ProjectId, a.PresenterId, a.CreatedAt);

    public PresenterProjectAssignment ToEntity() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        PresenterId = PresenterId,
        CreatedAt = CreatedAt
    };
}
