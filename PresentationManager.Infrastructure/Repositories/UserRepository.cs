using Microsoft.EntityFrameworkCore;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public UserRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<User>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username, ct);
    }

    public async Task<User?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramChatId == telegramChatId, ct);
    }

    public async Task<User?> GetByTelegramUsernameAsync(string telegramUsername, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var normalized = telegramUsername.ToLowerInvariant();
        return await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUsername != null && u.TelegramUsername.ToLower() == normalized, ct);
    }

    public async Task<User> AddAsync(User user, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users.CountAsync(ct);
    }

    public async Task SetTelegramLinkAsync(int userId, long telegramChatId, string? telegramUsername, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return;
        }

        user.TelegramChatId = telegramChatId;
        user.TelegramUsername = telegramUsername;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetPasswordAsync(int userId, string passwordHash, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return;
        }

        user.PasswordHash = passwordHash;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetUsernameAsync(int userId, string username, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return;
        }

        user.Username = username;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetRoleAsync(int userId, UserRole role, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return;
        }

        user.Role = role;
        await db.SaveChangesAsync(ct);
    }
}
