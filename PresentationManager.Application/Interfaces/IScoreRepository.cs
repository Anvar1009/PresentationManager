using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IScoreRepository
{
    Task<List<Score>> GetAllAsync(CancellationToken ct = default);

    Task<List<Score>> GetByPresentationAndJudgeAsync(int presentationId, int judgeId, CancellationToken ct = default);

    /// <summary>Every score for every presentation in <paramref name="presentationIds"/> — used to compute a
    /// whole project's final scores table in one query instead of one per presentation.</summary>
    Task<List<Score>> GetByPresentationIdsAsync(IReadOnlyList<int> presentationIds, CancellationToken ct = default);

    /// <summary>Inserts a new score or updates the existing one for the same (PresentationId, JudgeId,
    /// CriterionId) triple — a judge revising their own score is expected, not an error.</summary>
    Task UpsertAsync(int presentationId, int judgeId, int criterionId, int value, CancellationToken ct = default);
}
