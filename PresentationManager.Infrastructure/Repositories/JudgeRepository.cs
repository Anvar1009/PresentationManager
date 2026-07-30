using Microsoft.EntityFrameworkCore;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class JudgeRepository : IJudgeRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public JudgeRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<Judge>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Judges.AsNoTracking().OrderBy(j => j.ProjectId).ToListAsync(ct);
    }

    public async Task<List<Judge>> GetByProjectIdAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Judges.AsNoTracking().Where(j => j.ProjectId == projectId).ToListAsync(ct);
    }

    public async Task<List<Judge>> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Judges.AsNoTracking().Where(j => j.TelegramChatId == telegramChatId).ToListAsync(ct);
    }

    public async Task<Judge> AddAsync(Judge judge, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Judges.Add(judge);
        await db.SaveChangesAsync(ct);
        return judge;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Judges.FindAsync([id], ct);
        if (entity is null)
        {
            return;
        }

        db.Judges.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
