using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient.Wire;

/// <summary>Mirrors PresentationManager.API.Dtos.ProjectDto.</summary>
internal sealed record ProjectWire(
    int Id, string Name, DateOnly EventStartDate, DateOnly EventEndDate, TimeOnly? EventTime,
    string? Location, int? CreatedByUserId, DateTime CreatedAt, DateTime UpdatedAt)
{
    public Project ToEntity() => new()
    {
        Id = Id,
        Name = Name,
        EventStartDate = EventStartDate,
        EventEndDate = EventEndDate,
        EventTime = EventTime,
        Location = Location,
        CreatedByUserId = CreatedByUserId,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}

/// <summary>Mirrors PresentationManager.API.Dtos.CreateProjectRequest.</summary>
internal sealed record CreateProjectWireRequest(
    string Name, DateOnly EventStartDate, DateOnly EventEndDate, TimeOnly? EventTime,
    string? Location, int? CreatedByUserId)
{
    public static CreateProjectWireRequest FromEntity(Project p) => new(
        p.Name, p.EventStartDate, p.EventEndDate, p.EventTime, p.Location, p.CreatedByUserId);
}
