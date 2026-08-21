using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class ScoreRepository : IScoreRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ScoreRepository> _logger;

    public ScoreRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<ScoreRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<Score>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Scores.AsNoTracking().ToListAsync(ct);
    }

    public async Task<List<Score>> GetByPresentationAndJudgeAsync(int presentationId, int judgeId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Scores.AsNoTracking()
            .Where(s => s.PresentationId == presentationId && s.JudgeId == judgeId)
            .ToListAsync(ct);
    }

    public async Task<List<Score>> GetByPresentationIdsAsync(IReadOnlyList<int> presentationIds, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Scores.AsNoTracking()
            .Where(s => presentationIds.Contains(s.PresentationId))
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(int presentationId, int judgeId, int criterionId, int value, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Scores.FirstOrDefaultAsync(
            s => s.PresentationId == presentationId && s.JudgeId == judgeId && s.CriterionId == criterionId, ct);

        if (existing is null)
        {
            db.Scores.Add(new Score
            {
                PresentationId = presentationId,
                JudgeId = judgeId,
                CriterionId = criterionId,
                Value = value
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bahoni saqlashda xatolik: taqdimot {PresentationId}, hakam {JudgeId}, mezon {CriterionId}",
                presentationId, judgeId, criterionId);
            throw;
        }

        _logger.LogInformation("Baho qo'yildi: taqdimot {PresentationId}, hakam {JudgeId}, mezon {CriterionId} = {Value}",
            presentationId, judgeId, criterionId, value);
    }
}
