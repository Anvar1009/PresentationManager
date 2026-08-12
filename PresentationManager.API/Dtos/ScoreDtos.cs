using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Dtos;

public sealed record ScoreDto(int Id, int PresentationId, int JudgeId, int CriterionId, int Value, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static ScoreDto FromEntity(Score s) => new(s.Id, s.PresentationId, s.JudgeId, s.CriterionId, s.Value, s.CreatedAt, s.UpdatedAt);
}

public sealed record UpsertScoreRequest(int PresentationId, int JudgeId, int CriterionId, int Value);
