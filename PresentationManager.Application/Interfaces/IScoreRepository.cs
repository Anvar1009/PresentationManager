using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IScoreRepository
{
    Task<List<Score>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Every score whose presentation's project belongs to <paramref name="organizationId"/>, plus
    /// any whose project has no recorded organization (legacy rows) - a Manager's dashboard's org-scoped
    /// counterpart to <see cref="GetAllAsync"/>. See <see cref="Domain.Entities.Project.OrganizationId"/>.</summary>
    Task<List<Score>> GetByOrganizationAsync(int organizationId, CancellationToken ct = default);

    Task<List<Score>> GetByPresentationAndJudgeAsync(int presentationId, int judgeId, CancellationToken ct = default);

    /// <summary>Every score for every presentation in <paramref name="presentationIds"/> — used to compute a
    /// whole project's final scores table in one query instead of one per presentation.</summary>
    Task<List<Score>> GetByPresentationIdsAsync(IReadOnlyList<int> presentationIds, CancellationToken ct = default);

    /// <summary>Inserts a new score or updates the existing one for the same (PresentationId, JudgeId,
    /// CriterionId) triple — a judge revising their own score is expected, not an error.</summary>
    Task UpsertAsync(int presentationId, int judgeId, int criterionId, int value, CancellationToken ct = default);
}
