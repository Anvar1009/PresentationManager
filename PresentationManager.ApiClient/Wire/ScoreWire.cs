using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient.Wire;

/// <summary>Mirrors PresentationManager.API.Dtos.ScoreDto.</summary>
internal sealed record ScoreWire(int Id, int PresentationId, int JudgeId, int CriterionId, int Value, DateTime CreatedAt, DateTime UpdatedAt)
{
    public Score ToEntity() => new()
    {
        Id = Id,
        PresentationId = PresentationId,
        JudgeId = JudgeId,
        CriterionId = CriterionId,
        Value = Value,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}

internal sealed record UpsertScoreWireRequest(int PresentationId, int JudgeId, int CriterionId, int Value);
