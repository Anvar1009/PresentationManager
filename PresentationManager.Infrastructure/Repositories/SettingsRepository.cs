using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<SettingsRepository> _logger;

    public SettingsRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<SettingsRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (settings is not null)
        {
            return settings;
        }

        settings = new AppSettings { Id = 1 };
        db.Settings.Add(settings);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sozlamalarni birinchi marta yaratishda xatolik");
            throw;
        }

        _logger.LogInformation("Standart sozlamalar yaratildi");
        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        settings.Id = 1;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var exists = await db.Settings.AsNoTracking().AnyAsync(s => s.Id == 1, ct);
        if (exists)
        {
            db.Settings.Update(settings);
        }
        else
        {
            db.Settings.Add(settings);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sozlamalarni saqlashda xatolik");
            throw;
        }

        _logger.LogInformation("Sozlamalar saqlandi");
    }
}
