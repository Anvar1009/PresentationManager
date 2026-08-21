using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class HistoryRepository : IHistoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<HistoryRepository> _logger;

    public HistoryRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<HistoryRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task AddAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.HistoryEntries.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tarix yozuvini saqlashda xatolik: taqdimot {PresentationId}", entry.PresentationId);
            throw;
        }
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
        int deletedCount;
        try
        {
            deletedCount = await db.HistoryEntries.ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tarixni tozalashda xatolik");
            throw;
        }

        _logger.LogInformation("Tarix tozalandi: {DeletedCount} ta yozuv o'chirildi", deletedCount);
    }
}
