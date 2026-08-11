using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.Application.Interfaces;

public interface IUserRepository
{
    Task<List<User>> GetAllAsync(CancellationToken ct = default);

    Task<User?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);

    Task<User?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default);

    /// <summary>Case-insensitive lookup - see <see cref="User.TelegramUsername"/>.</summary>
    Task<User?> GetByTelegramUsernameAsync(string telegramUsername, CancellationToken ct = default);

    Task<User> AddAsync(User user, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Links (or re-links) this Admin/Operator's desktop account to a Telegram chat - see
    /// <see cref="User.TelegramChatId"/>/<see cref="User.TelegramUsername"/>.</summary>
    Task SetTelegramLinkAsync(int userId, long telegramChatId, string? telegramUsername, CancellationToken ct = default);

    /// <summary>Stores (or clears, passing null for both) the pending "Botga ulash" deep-link token - see
    /// <see cref="User.TelegramLinkToken"/>.</summary>
    Task SetTelegramLinkTokenAsync(int userId, string? token, DateTime? expiresAtUtc, CancellationToken ct = default);

    /// <summary>Looks up whichever user currently holds this pending link token - does not check expiry
    /// itself, since <c>AdminLinkService</c> needs the expiry timestamp to decide that.</summary>
    Task<User?> GetByTelegramLinkTokenAsync(string token, CancellationToken ct = default);

    Task SetPasswordAsync(int userId, string passwordHash, CancellationToken ct = default);

    /// <summary>SuperAdmin-driven login change — see <see cref="PresentationManager.Application.Services.UserService.EditUserAsync"/>.</summary>
    Task SetUsernameAsync(int userId, string username, CancellationToken ct = default);

    /// <summary>SuperAdmin-driven role change — see <see cref="PresentationManager.Application.Services.UserService.ChangeRoleAsync"/>.</summary>
    Task SetRoleAsync(int userId, UserRole role, CancellationToken ct = default);
}
