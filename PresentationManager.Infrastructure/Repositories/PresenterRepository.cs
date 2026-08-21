using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class PresenterRepository : IPresenterRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<PresenterRepository> _logger;

    public PresenterRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<PresenterRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<Presenter>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Presenters.AsNoTracking().OrderBy(p => p.FullName).ToListAsync(ct);
    }

    public async Task<Presenter?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Presenters.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Presenter?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Presenters.AsNoTracking().FirstOrDefaultAsync(p => p.TelegramChatId == telegramChatId, ct);
    }

    public async Task<Presenter> AddAsync(Presenter presenter, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Presenters.Add(presenter);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Taqdimotchini saqlashda xatolik: {FullName}", presenter.FullName);
            throw;
        }

        _logger.LogInformation("Taqdimotchi bazaga yozildi: {PresenterId} - {FullName}", presenter.Id, presenter.FullName);
        return presenter;
    }
}
