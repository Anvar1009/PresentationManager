using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Dtos;

public sealed record ProjectDto(
    int Id,
    string Name,
    DateOnly EventStartDate,
    DateOnly EventEndDate,
    TimeOnly? EventTime,
    string? Location,
    int? CreatedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static ProjectDto FromEntity(Project project) => new(
        project.Id, project.Name, project.EventStartDate, project.EventEndDate,
        project.EventTime, project.Location, project.CreatedByUserId, project.CreatedAt, project.UpdatedAt);
}

public sealed record CreateProjectRequest(
    string Name,
    DateOnly EventStartDate,
    DateOnly EventEndDate,
    TimeOnly? EventTime,
    string? Location,
    int? CreatedByUserId);
