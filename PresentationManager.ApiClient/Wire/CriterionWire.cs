using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient.Wire;

/// <summary>Mirrors PresentationManager.API.Dtos.CriterionDto.</summary>
internal sealed record CriterionWire(int Id, int ProjectId, string Name, int MaxScore, int OrderNumber, DateTime CreatedAt)
{
    public static CriterionWire FromEntity(EvaluationCriterion c) => new(c.Id, c.ProjectId, c.Name, c.MaxScore, c.OrderNumber, c.CreatedAt);

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
