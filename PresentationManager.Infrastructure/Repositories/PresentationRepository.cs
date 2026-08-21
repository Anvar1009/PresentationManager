using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<PresentationRepository> _logger;

    public PresentationRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<PresentationRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<Presentation>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Presentations.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Taqdimotni saqlashda xatolik: loyiha {ProjectId}", presentation.ProjectId);
            throw;
        }

        _logger.LogInformation("Taqdimot bazaga yozildi: {PresentationId} - {Title}", presentation.Id, presentation.Title);
        return presentation;
    }

    public async Task UpdateAsync(Presentation presentation, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Presentations.Update(presentation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Taqdimotni yangilashda xatolik: {PresentationId}", presentation.Id);
            throw;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Presentations.FindAsync([id], ct);
        if (entity is null)
        {
            _logger.LogWarning("O'chirish uchun taqdimot topilmadi: {PresentationId}", id);
            return;
        }

        db.Presentations.Remove(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Taqdimotni o'chirishda xatolik: {PresentationId}", id);
            throw;
        }

        _logger.LogInformation("Taqdimot bazadan o'chirildi: {PresentationId}", id);
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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Navbatni qayta tartiblashda xatolik: {Count} ta taqdimot", orderedPresentationIds.Count);
            throw;
        }
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
