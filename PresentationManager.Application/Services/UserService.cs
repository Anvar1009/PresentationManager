using System.Security.Cryptography;
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

    /// <summary>SuperAdmin panel's "+ Foydalanuvchi qo'shish" action — creates a login account for someone who
    /// already registered through the Telegram bot as a <see cref="Presenter"/>, instead of retyping their name
    /// and inventing a login/password by hand. Username and password are both generated here and must be
    /// delivered to the presenter out of band (the caller sends them over the Telegram chat this presenter
    /// already registered from) since nothing about them is chosen or memorable.</summary>
    public async Task<(User User, string GeneratedPassword)> CreateFromPresenterAsync(Presenter presenter, UserRole role, CancellationToken ct = default)
    {
        var existing = await _userRepository.GetByTelegramChatIdAsync(presenter.TelegramChatId, ct);
        if (existing is not null)
        {
            throw new InvalidOperationException("Bu taqdimotchi allaqachon tizim foydalanuvchisi.");
        }

        var username = await GenerateUniqueUsernameAsync(presenter.PhoneNumber, ct);
        var password = GenerateRandomPassword();

        var user = new User
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            FullName = presenter.FullName,
            Role = role,
            TelegramChatId = presenter.TelegramChatId,
            TelegramUsername = presenter.TelegramUsername
        };

        var created = await _userRepository.AddAsync(user, ct);
        return (created, password);
    }

    private async Task<string> GenerateUniqueUsernameAsync(string? phoneNumber, CancellationToken ct)
    {
        var digits = phoneNumber is null ? "" : new string(phoneNumber.Where(char.IsDigit).ToArray());
        var baseUsername = digits.Length > 0 ? digits : "user";

        var candidate = baseUsername;
        var suffix = 1;
        while (await _userRepository.GetByUsernameAsync(candidate, ct) is not null)
        {
            candidate = $"{baseUsername}{++suffix}";
        }

        return candidate;
    }

    /// <summary>8 random characters from an unambiguous alphabet (no 0/O/1/I/l) since this is read off a phone
    /// screen and typed once, not chosen or memorized.</summary>
    private static string GenerateRandomPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return new string(RandomNumberGenerator.GetBytes(8).Select(b => chars[b % chars.Length]).ToArray());
    }

    /// <summary>SuperAdmin panel's Rol dropdown on an existing account — the only way a role ever changes after
    /// creation.</summary>
    public Task ChangeRoleAsync(int userId, UserRole newRole, CancellationToken ct = default) =>
        _userRepository.SetRoleAsync(userId, newRole, ct);

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
