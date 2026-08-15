using Microsoft.EntityFrameworkCore;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class PresenterProjectAssignmentRepository : IPresenterProjectAssignmentRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public PresenterProjectAssignmentRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<PresenterProjectAssignment>> GetByProjectIdAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PresenterProjectAssignments.AsNoTracking().Where(a => a.ProjectId == projectId).ToListAsync(ct);
    }

    public async Task<List<PresenterProjectAssignment>> GetByPresenterIdAsync(int presenterId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PresenterProjectAssignments.AsNoTracking().Where(a => a.PresenterId == presenterId).ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(int projectId, int presenterId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PresenterProjectAssignments.AsNoTracking()
            .AnyAsync(a => a.ProjectId == projectId && a.PresenterId == presenterId, ct);
    }

    public async Task<PresenterProjectAssignment> AddAsync(PresenterProjectAssignment assignment, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.PresenterProjectAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        return assignment;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.PresenterProjectAssignments.FindAsync([id], ct);
        if (entity is null)
        {
            return;
        }

        db.PresenterProjectAssignments.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
