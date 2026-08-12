using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.ApiClient.Wire;

/// <summary>Mirrors PresentationManager.API.Dtos.UserDto - every read from the API comes back in this
/// shape, which never carries a password hash or Telegram link token (see HttpUserRepository/
/// HttpAuthService for why nothing on this side needs either).</summary>
internal sealed record UserWire(
    int Id, string Username, string FullName, UserRole Role, bool IsActive,
    long? TelegramChatId, string? TelegramUsername)
{
    public User ToEntity() => new()
    {
        Id = Id,
        Username = Username,
        PasswordHash = string.Empty,
        FullName = FullName,
        Role = Role,
        IsActive = IsActive,
        TelegramChatId = TelegramChatId,
        TelegramUsername = TelegramUsername
    };
}

/// <summary>Mirrors PresentationManager.API.Dtos.CreateUserRequest - the only request that has to carry a
/// password hash across the wire, since UserService already computed it client-side before calling
/// IUserRepository.AddAsync.</summary>
internal sealed record CreateUserWireRequest(
    string Username, string PasswordHash, string FullName, UserRole Role, bool IsActive,
    long? TelegramChatId, string? TelegramUsername)
{
    public static CreateUserWireRequest FromEntity(User u) => new(
        u.Username, u.PasswordHash, u.FullName, u.Role, u.IsActive, u.TelegramChatId, u.TelegramUsername);
}
