using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class PresenterUploadTokenRepository : IPresenterUploadTokenRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<PresenterUploadTokenRepository> _logger;

    public PresenterUploadTokenRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<PresenterUploadTokenRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<PresenterUploadToken> AddAsync(PresenterUploadToken token, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.PresenterUploadTokens.Add(token);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Taqdimotchi uchun yuklash havolasi yaratildi: chat {ChatId}, presenter {PresenterId}", token.ChatId, token.PresenterId);
        return token;
    }

    public async Task<PresenterUploadToken?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PresenterUploadTokens.FirstOrDefaultAsync(t => t.Token == token, ct);
    }
}
