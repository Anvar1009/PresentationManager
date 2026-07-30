using Microsoft.EntityFrameworkCore;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class ScoreRepository : IScoreRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ScoreRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
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

        await db.SaveChangesAsync(ct);
    }
}
