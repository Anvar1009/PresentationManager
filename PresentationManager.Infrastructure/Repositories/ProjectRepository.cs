using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ProjectRepository> _logger;

    public ProjectRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<ProjectRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<Project>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Projects.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
    }

    public async Task<List<Project>> GetByCreatorAsync(int createdByUserId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Projects.AsNoTracking()
            .Where(p => p.CreatedByUserId == createdByUserId || p.CreatedByUserId == null)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<Project?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Project> AddAsync(Project project, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Projects.Add(project);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loyihani saqlashda xatolik: {ProjectName}", project.Name);
            throw;
        }

        _logger.LogInformation("Loyiha bazaga yozildi: {ProjectId} - {ProjectName}", project.Id, project.Name);
        return project;
    }

    public async Task UpdateAsync(Project project, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Projects.Update(project);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loyihani yangilashda xatolik: {ProjectId}", project.Id);
            throw;
        }

        _logger.LogInformation("Loyiha yangilandi: {ProjectId}", project.Id);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Projects.FindAsync([id], ct);
        if (entity is null)
        {
            _logger.LogWarning("O'chirish uchun loyiha topilmadi: {ProjectId}", id);
            return;
        }

        db.Projects.Remove(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loyihani o'chirishda xatolik: {ProjectId}", id);
            throw;
        }

        _logger.LogInformation("Loyiha bazadan o'chirildi: {ProjectId}", id);
    }
}
