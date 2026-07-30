using Microsoft.EntityFrameworkCore;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class CriterionRepository : ICriterionRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public CriterionRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
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
        await db.SaveChangesAsync(ct);
        return criterion;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.EvaluationCriteria.FindAsync([id], ct);
        if (entity is null)
        {
            return;
        }

        db.EvaluationCriteria.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
