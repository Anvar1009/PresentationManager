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
