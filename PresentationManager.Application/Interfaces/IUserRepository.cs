using PresentationManager.Domain.Entities;

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

    Task SetPasswordAsync(int userId, string passwordHash, CancellationToken ct = default);

    /// <summary>SuperAdmin-driven login change — see <see cref="PresentationManager.Application.Services.UserService.EditUserAsync"/>.</summary>
    Task SetUsernameAsync(int userId, string username, CancellationToken ct = default);
}
