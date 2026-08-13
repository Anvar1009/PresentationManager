using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Dtos;

/// <summary>Unlike <see cref="UserDto"/> (which never carries a password hash), this one has to - the hash
/// is already computed client-side (PresentationManager.Application.Common.PasswordHasher, invoked by
/// UserService.CreateAsync/CreateFromPresenterAsync, both of which keep running in
/// PresentationManager.UI unchanged) before IUserRepository.AddAsync is ever called, so the HTTP call has
/// no choice but to carry the already-hashed value across. Only used as a request body for POST - every
/// response (including the one this same POST returns) uses the safe <see cref="UserDto"/> instead.</summary>
public sealed record CreateUserRequest(
    string Username,
    string PasswordHash,
    string FullName,
    UserRole Role,
    bool IsActive,
    long? TelegramChatId,
    string? TelegramUsername)
{
    public User ToEntity() => new()
    {
        Username = Username,
        PasswordHash = PasswordHash,
        FullName = FullName,
        Role = Role,
        IsActive = IsActive,
        TelegramChatId = TelegramChatId,
        TelegramUsername = TelegramUsername
    };
}

public sealed record SetTelegramLinkRequest(long TelegramChatId, string? TelegramUsername);

public sealed record SetTelegramLinkTokenRequest(string? Token, DateTime? ExpiresAtUtc);

/// <summary>Just enough for AdminLinkService.TryConsumeAsync's own expiry check - never echoes the token
/// string itself back (the caller already has it, since they're the one looking it up).</summary>
public sealed record TelegramLinkTokenLookupDto(int UserId, DateTime? ExpiresAtUtc);

public sealed record SetPasswordRequest(string PasswordHash);

public sealed record SetUsernameRequest(string Username);

public sealed record SetFullNameRequest(string FullName);

public sealed record SetRoleRequest(UserRole Role);
