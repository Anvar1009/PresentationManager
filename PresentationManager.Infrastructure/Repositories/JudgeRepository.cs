using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class JudgeRepository : IJudgeRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<JudgeRepository> _logger;

    public JudgeRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<JudgeRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hakamni saqlashda xatolik: loyiha {ProjectId}", judge.ProjectId);
            throw;
        }

        _logger.LogInformation("Hakam bazaga yozildi: {JudgeId} - loyiha {ProjectId}", judge.Id, judge.ProjectId);
        return judge;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Judges.FindAsync([id], ct);
        if (entity is null)
        {
            _logger.LogWarning("O'chirish uchun hakam topilmadi: {JudgeId}", id);
            return;
        }

        db.Judges.Remove(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hakamni o'chirishda xatolik: {JudgeId}", id);
            throw;
        }

        _logger.LogInformation("Hakam bazadan o'chirildi: {JudgeId}", id);
    }
}
