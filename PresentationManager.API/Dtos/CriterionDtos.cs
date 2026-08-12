using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Dtos;

public sealed record CriterionDto(int Id, int ProjectId, string Name, int MaxScore, int OrderNumber, DateTime CreatedAt)
{
    public static CriterionDto FromEntity(EvaluationCriterion c) => new(c.Id, c.ProjectId, c.Name, c.MaxScore, c.OrderNumber, c.CreatedAt);

    public EvaluationCriterion ToEntity() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        Name = Name,
        MaxScore = MaxScore,
        OrderNumber = OrderNumber,
        CreatedAt = CreatedAt
    };
}
