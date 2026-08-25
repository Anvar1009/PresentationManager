using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class OrganizationRepository : IOrganizationRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<OrganizationRepository> _logger;

    public OrganizationRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<OrganizationRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<Organization>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Organizations.AsNoTracking().OrderBy(o => o.Name).ToListAsync(ct);
    }

    public async Task<Organization?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task<Organization> AddAsync(Organization organization, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Organizations.Add(organization);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tashkilotni saqlashda xatolik: {Name}", organization.Name);
            throw;
        }

        _logger.LogInformation("Tashkilot yaratildi: {OrganizationId} - {Name}", organization.Id, organization.Name);
        return organization;
    }

    public async Task RenameAsync(int id, string name, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var organization = await db.Organizations.FindAsync([id], ct);
        if (organization is null)
        {
            _logger.LogWarning("Nomini o'zgartirish uchun tashkilot topilmadi: {OrganizationId}", id);
            return;
        }

        organization.Name = name;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Tashkilot nomi o'zgartirildi: {OrganizationId} -> {Name}", id, name);
    }
}
