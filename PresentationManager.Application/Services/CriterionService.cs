using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Services;

/// <summary>Admin-facing CRUD over a project's judging criteria (dynamic per project, not a fixed rubric).</summary>
public sealed class CriterionService
{
    private readonly ICriterionRepository _criterionRepository;
    private readonly ILogger<CriterionService> _logger;

    public CriterionService(ICriterionRepository criterionRepository, ILogger<CriterionService> logger)
    {
        _criterionRepository = criterionRepository;
        _logger = logger;
    }

    public Task<List<EvaluationCriterion>> GetAllAsync(CancellationToken ct = default) => _criterionRepository.GetAllAsync(ct);

    public Task<List<EvaluationCriterion>> GetByProjectIdAsync(int projectId, CancellationToken ct = default) =>
        _criterionRepository.GetByProjectIdAsync(projectId, ct);

    public async Task<EvaluationCriterion> CreateAsync(int projectId, string name, int maxScore, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("Mezon yaratishga urinish bo'sh nom bilan rad etildi: loyiha {ProjectId}", projectId);
            throw new InvalidOperationException("Mezon nomi bo'sh bo'lishi mumkin emas.");
        }

        if (maxScore < 1)
        {
            _logger.LogWarning("Mezon yaratishga urinish noto'g'ri maksimal ball bilan rad etildi: {MaxScore}", maxScore);
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
        var created = await _criterionRepository.AddAsync(criterion, ct);
        _logger.LogInformation("Yangi mezon yaratildi: {CriterionId} - {CriterionName} (loyiha {ProjectId})",
            created.Id, created.Name, projectId);
        return created;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await _criterionRepository.DeleteAsync(id, ct);
        _logger.LogInformation("Mezon o'chirildi: {CriterionId}", id);
    }
}
