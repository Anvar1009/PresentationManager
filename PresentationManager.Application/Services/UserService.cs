using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.Application.Services;

/// <summary>Desktop login (Operator/Admin/SuperAdmin only — Presenters/Judges never authenticate here, they
/// only ever go through the Telegram bot).</summary>
public sealed class UserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public Task<List<User>> GetAllAsync(CancellationToken ct = default) => _userRepository.GetAllAsync(ct);

    public Task<User?> GetByIdAsync(int id, CancellationToken ct = default) => _userRepository.GetByIdAsync(id, ct);

    /// <summary>Looks up which Admin/Operator (if any) a Telegram chat is linked to - see
    /// <see cref="User.TelegramChatId"/>.</summary>
    public Task<User?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default) =>
        _userRepository.GetByTelegramChatIdAsync(telegramChatId, ct);

    /// <summary>Used by "Parolni unutdingizmi?" to find which account a Telegram @username belongs to - see
    /// <see cref="User.TelegramUsername"/>.</summary>
    public Task<User?> GetByTelegramUsernameAsync(string telegramUsername, CancellationToken ct = default) =>
        _userRepository.GetByTelegramUsernameAsync(telegramUsername, ct);

    public Task LinkTelegramChatAsync(int userId, long telegramChatId, string? telegramUsername, CancellationToken ct = default) =>
        _userRepository.SetTelegramLinkAsync(userId, telegramChatId, telegramUsername, ct);

    /// <summary>Used by the "Parolni unutdingizmi?" flow once the Telegram-delivered code has been verified
    /// (<see cref="PasswordResetService"/>) - never called with an unverified code.</summary>
    public async Task ResetPasswordAsync(int userId, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            throw new InvalidOperationException("Parol kamida 6 ta belgidan iborat bo'lishi kerak.");
        }

        await _userRepository.SetPasswordAsync(userId, PasswordHasher.Hash(newPassword), ct);
    }

    public async Task<User?> ValidateLoginAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByUsernameAsync(username, ct);
        if (user is null || !user.IsActive || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        return user;
    }

    public async Task<User> CreateAsync(string username, string password, string fullName, UserRole role, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException("Barcha maydonlar to'ldirilishi shart.");
        }

        var existing = await _userRepository.GetByUsernameAsync(username, ct);
        if (existing is not null)
        {
            throw new InvalidOperationException("Bu login band.");
        }

        var user = new User
        {
            Username = username.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            FullName = fullName.Trim(),
            Role = role
        };
        return await _userRepository.AddAsync(user, ct);
    }

    /// <summary>SuperAdmin panel's "Login/parolni tiklash" action on an existing account — the recovery path
    /// for a user who forgot their login/password and has no Telegram link for the bot-side "Parolni
    /// unutdingizmi?" flow. <paramref name="newPassword"/> null/blank keeps the current password unchanged;
    /// the login is always updated.</summary>
    public async Task EditUserAsync(int userId, string newUsername, string? newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newUsername))
        {
            throw new InvalidOperationException("Login bo'sh bo'lishi mumkin emas.");
        }

        var trimmedUsername = newUsername.Trim();
        var existing = await _userRepository.GetByUsernameAsync(trimmedUsername, ct);
        if (existing is not null && existing.Id != userId)
        {
            throw new InvalidOperationException("Bu login band.");
        }

        await _userRepository.SetUsernameAsync(userId, trimmedUsername, ct);

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            if (newPassword.Length < 6)
            {
                throw new InvalidOperationException("Parol kamida 6 ta belgidan iborat bo'lishi kerak.");
            }

            await _userRepository.SetPasswordAsync(userId, PasswordHasher.Hash(newPassword), ct);
        }
    }

    /// <summary>Bootstraps the very first account so the newly-added login requirement doesn't lock everyone
    /// out on an empty database — called once at startup. Every account after this one is created from the
    /// SuperAdmin panel's Users tab.</summary>
    public async Task EnsureDefaultSuperAdminAsync(CancellationToken ct = default)
    {
        if (await _userRepository.CountAsync(ct) > 0)
        {
            return;
        }

        await CreateAsync("superadmin", "admin123", "Super Administrator", UserRole.SuperAdmin, ct);
    }
}
