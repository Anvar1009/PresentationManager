using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(IDbContextFactory<AppDbContext> dbFactory, ILogger<UserRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Foydalanuvchini saqlashda xatolik: {Username}", user.Username);
            throw;
        }

        _logger.LogInformation("Foydalanuvchi yaratildi: {UserId} - {Username} ({Role})", user.Id, user.Username, user.Role);
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
            _logger.LogWarning("Telegram bog'lash uchun foydalanuvchi topilmadi: {UserId}", userId);
            return;
        }

        user.TelegramChatId = telegramChatId;
        user.TelegramUsername = telegramUsername;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Foydalanuvchi {UserId} Telegramga bog'landi: {TelegramChatId}", userId, telegramChatId);
    }

    public async Task SetTelegramLinkTokenAsync(int userId, string? token, DateTime? expiresAtUtc, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            _logger.LogWarning("Bog'lash tokenini o'rnatish uchun foydalanuvchi topilmadi: {UserId}", userId);
            return;
        }

        user.TelegramLinkToken = token;
        user.TelegramLinkTokenExpiresAtUtc = expiresAtUtc;
        await db.SaveChangesAsync(ct);
    }

    public async Task<User?> GetByTelegramLinkTokenAsync(string token, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramLinkToken == token, ct);
    }

    public async Task SetPasswordAsync(int userId, string passwordHash, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            _logger.LogWarning("Parol o'rnatish uchun foydalanuvchi topilmadi: {UserId}", userId);
            return;
        }

        user.PasswordHash = passwordHash;
        await db.SaveChangesAsync(ct);
        // Never log the hash/plaintext itself - only that a change happened and for whom.
        _logger.LogInformation("Foydalanuvchi {UserId} paroli o'zgartirildi", userId);
    }

    public async Task SetUsernameAsync(int userId, string username, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            _logger.LogWarning("Login o'rnatish uchun foydalanuvchi topilmadi: {UserId}", userId);
            return;
        }

        user.Username = username;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Foydalanuvchi {UserId} logini o'zgartirildi: {Username}", userId, username);
    }

    public async Task SetFullNameAsync(int userId, string fullName, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            _logger.LogWarning("F.I.Sh. o'rnatish uchun foydalanuvchi topilmadi: {UserId}", userId);
            return;
        }

        user.FullName = fullName;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetRoleAsync(int userId, UserRole role, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            _logger.LogWarning("Rol o'rnatish uchun foydalanuvchi topilmadi: {UserId}", userId);
            return;
        }

        user.Role = role;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Foydalanuvchi {UserId} roli o'zgartirildi: {Role}", userId, role);
    }
}
