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
    DateTime? SubmissionDeadline,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static ProjectDto FromEntity(Project project) => new(
        project.Id, project.Name, project.EventStartDate, project.EventEndDate,
        project.EventTime, project.Location, project.CreatedByUserId, project.SubmissionDeadline,
        project.CreatedAt, project.UpdatedAt);
}

public sealed record CreateProjectRequest(
    string Name,
    DateOnly EventStartDate,
    DateOnly EventEndDate,
    TimeOnly? EventTime,
    string? Location,
    int? CreatedByUserId);

/// <summary>Full-entity replace, mirroring <see cref="Application.Interfaces.IProjectRepository.UpdateAsync"/> -
/// <see cref="CreatedByUserId"/>/<see cref="CreatedAt"/> aren't included since nothing should ever change
/// them after creation.</summary>
public sealed record UpdateProjectRequest(
    string Name,
    DateOnly EventStartDate,
    DateOnly EventEndDate,
    TimeOnly? EventTime,
    string? Location,
    DateTime? SubmissionDeadline);
