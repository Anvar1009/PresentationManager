using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Services;

/// <summary>Admin-facing CRUD over a project's judging criteria (dynamic per project, not a fixed rubric).</summary>
public sealed class CriterionService
{
    private readonly ICriterionRepository _criterionRepository;

    public CriterionService(ICriterionRepository criterionRepository)
    {
        _criterionRepository = criterionRepository;
    }

    public Task<List<EvaluationCriterion>> GetAllAsync(CancellationToken ct = default) => _criterionRepository.GetAllAsync(ct);

    public Task<List<EvaluationCriterion>> GetByProjectIdAsync(int projectId, CancellationToken ct = default) =>
        _criterionRepository.GetByProjectIdAsync(projectId, ct);

    public async Task<EvaluationCriterion> CreateAsync(int projectId, string name, int maxScore, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Mezon nomi bo'sh bo'lishi mumkin emas.");
        }

        if (maxScore < 1)
        {
            throw new InvalidOperationException("Maksimal ball kamida 1 bo'lishi kerak.");
        }

        var existing = await _criterionRepository.GetByProjectIdAsync(projectId, ct);
        var criterion = new EvaluationCriterion
        {
            ProjectId = projectId,
            Name = name.Trim(),
            MaxScore = maxScore,
            OrderNumber = existing.Count
        };
        return await _criterionRepository.AddAsync(criterion, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct = default) => _criterionRepository.DeleteAsync(id, ct);
}
