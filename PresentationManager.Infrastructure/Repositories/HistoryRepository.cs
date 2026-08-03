using Microsoft.EntityFrameworkCore;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class HistoryRepository : IHistoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public HistoryRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task AddAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.HistoryEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<HistoryEntry>> GetRecentAsync(int count = 200, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.HistoryEntries.AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<List<HistoryEntry>> GetForPresentationAsync(int presentationId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.HistoryEntries.AsNoTracking()
            .Where(h => h.PresentationId == presentationId)
            .OrderBy(h => h.Timestamp)
            .ToListAsync(ct);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.HistoryEntries.ExecuteDeleteAsync(ct);
    }
}
