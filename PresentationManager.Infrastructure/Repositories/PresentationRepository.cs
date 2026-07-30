using Microsoft.EntityFrameworkCore;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

/// <summary>
/// Uses <see cref="IDbContextFactory{TContext}"/> to create a short-lived DbContext per operation rather
/// than holding one for the app's lifetime — this is a long-running WinForms process, so a single shared
/// context would accumulate tracked entities and risk stale-tracking bugs across admin actions.
/// </summary>
public class PresentationRepository : IPresentationRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public PresentationRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<Presentation>> GetAllOrderedAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Presentations.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.OrderNumber)
            .ToListAsync(ct);
    }

    public async Task<List<Presentation>> GetByProjectIdAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Presentations.AsNoTracking().Where(p => p.ProjectId == projectId).ToListAsync(ct);
    }

    public async Task<Presentation?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Presentations.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Presentation> AddAsync(Presentation presentation, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Presentations.Add(presentation);
        await db.SaveChangesAsync(ct);
        return presentation;
    }

    public async Task UpdateAsync(Presentation presentation, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Presentations.Update(presentation);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Presentations.FindAsync([id], ct);
        if (entity is null)
        {
            return;
        }

        db.Presentations.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(IReadOnlyList<int> orderedPresentationIds, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var presentations = await db.Presentations
            .Where(p => orderedPresentationIds.Contains(p.Id))
            .ToListAsync(ct);

        var byId = presentations.ToDictionary(p => p.Id);
        for (var i = 0; i < orderedPresentationIds.Count; i++)
        {
            if (byId.TryGetValue(orderedPresentationIds[i], out var presentation))
            {
                presentation.OrderNumber = i;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Presentation>> SearchByNameAsync(string query, int projectId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Presentations.AsNoTracking()
            .Where(p => p.ProjectId == projectId && EF.Functions.Like(p.FullName, $"%{query}%"))
            .OrderBy(p => p.OrderNumber)
            .ToListAsync(ct);
    }
}
