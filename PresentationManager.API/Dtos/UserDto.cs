using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Dtos;

/// <summary>Never carries <see cref="User.PasswordHash"/> or <see cref="User.TelegramLinkToken"/> -
/// those never leave this process.</summary>
public sealed record UserDto(
    int Id,
    string Username,
    string FullName,
    UserRole Role,
    bool IsActive,
    long? TelegramChatId,
    string? TelegramUsername)
{
    public static UserDto FromEntity(User user) => new(
        user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.TelegramChatId, user.TelegramUsername);
}
