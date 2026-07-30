using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface ICriterionRepository
{
    Task<List<EvaluationCriterion>> GetAllAsync(CancellationToken ct = default);

    Task<List<EvaluationCriterion>> GetByProjectIdAsync(int projectId, CancellationToken ct = default);

    Task<EvaluationCriterion> AddAsync(EvaluationCriterion criterion, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
