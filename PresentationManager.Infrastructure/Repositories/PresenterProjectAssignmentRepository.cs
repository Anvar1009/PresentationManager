using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class PresenterProjectAssignmentRepository : IPresenterProjectAssignmentRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<PresenterProjectAssignmentRepository> _logger;

    public PresenterProjectAssignmentRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<PresenterProjectAssignmentRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Biriktirishni saqlashda xatolik: taqdimotchi {PresenterId}, loyiha {ProjectId}",
                assignment.PresenterId, assignment.ProjectId);
            throw;
        }

        _logger.LogInformation("Taqdimotchi loyihaga biriktirildi: {PresenterId} -> loyiha {ProjectId}",
            assignment.PresenterId, assignment.ProjectId);
        return assignment;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.PresenterProjectAssignments.FindAsync([id], ct);
        if (entity is null)
        {
            _logger.LogWarning("O'chirish uchun biriktirish topilmadi: {AssignmentId}", id);
            return;
        }

        db.PresenterProjectAssignments.Remove(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Biriktirishni o'chirishda xatolik: {AssignmentId}", id);
            throw;
        }

        _logger.LogInformation("Biriktirish o'chirildi: {AssignmentId}", id);
    }
}
