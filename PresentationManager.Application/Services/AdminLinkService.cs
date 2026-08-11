using System.Security.Cryptography;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.Application.Services;

/// <summary>Issues short-lived, single-use tokens that let <c>AdminPanelForm</c>'s "Botga ulash" button link
/// an Admin's desktop account to a Telegram chat, without ever putting a password through the bot. Backed by
/// a pair of columns on <c>User</c> (rather than an in-memory dictionary) since the token is created in one
/// process (the desktop app) and consumed in a different one (the Telegram bot's own worker process) - they
/// no longer share a DI container. A token that's never consumed (the Admin never taps the link) simply sits
/// expired until the next "Botga ulash" click overwrites it.</summary>
public sealed class AdminLinkService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    private readonly IUserRepository _userRepository;

    public AdminLinkService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<string> CreateTokenAsync(int userId, CancellationToken ct = default)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        await _userRepository.SetTelegramLinkTokenAsync(userId, token, DateTime.UtcNow.Add(TokenLifetime), ct);
        return token;
    }

    /// <summary>Consumes (clears) the token if it exists and hasn't expired - a token only ever works once,
    /// whether or not it succeeds here.</summary>
    public async Task<int?> TryConsumeAsync(string token, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByTelegramLinkTokenAsync(token, ct);
        if (user is null || user.TelegramLinkTokenExpiresAtUtc is not { } expiresAtUtc || expiresAtUtc <= DateTime.UtcNow)
        {
            return null;
        }

        await _userRepository.SetTelegramLinkTokenAsync(user.Id, null, null, ct);
        return user.Id;
    }
}
