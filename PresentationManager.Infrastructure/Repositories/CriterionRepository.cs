using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class CriterionRepository : ICriterionRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<CriterionRepository> _logger;

    public CriterionRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<CriterionRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<EvaluationCriterion>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.EvaluationCriteria.AsNoTracking().OrderBy(c => c.ProjectId).ThenBy(c => c.OrderNumber).ToListAsync(ct);
    }

    public async Task<List<EvaluationCriterion>> GetByProjectIdAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.EvaluationCriteria.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.OrderNumber)
            .ToListAsync(ct);
    }

    public async Task<EvaluationCriterion> AddAsync(EvaluationCriterion criterion, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.EvaluationCriteria.Add(criterion);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mezonni saqlashda xatolik: loyiha {ProjectId}", criterion.ProjectId);
            throw;
        }

        _logger.LogInformation("Mezon bazaga yozildi: {CriterionId} - loyiha {ProjectId}", criterion.Id, criterion.ProjectId);
        return criterion;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.EvaluationCriteria.FindAsync([id], ct);
        if (entity is null)
        {
            _logger.LogWarning("O'chirish uchun mezon topilmadi: {CriterionId}", id);
            return;
        }

        db.EvaluationCriteria.Remove(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mezonni o'chirishda xatolik: {CriterionId}", id);
            throw;
        }

        _logger.LogInformation("Mezon bazadan o'chirildi: {CriterionId}", id);
    }
}
