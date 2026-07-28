using Microsoft.EntityFrameworkCore;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SettingsRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
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
        await db.SaveChangesAsync(ct);
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

        await db.SaveChangesAsync(ct);
    }
}
