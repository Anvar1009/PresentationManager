using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PresentationManager.Application.Services;

/// <summary>Issues short-lived, single-use tokens that let <c>AdminPanelForm</c>'s "Botga ulash" button link
/// an Admin's desktop account to a Telegram chat, without ever putting a password through the bot. The
/// desktop app and the bot run embedded in the same process/DI container (see <c>Program.cs</c>), so an
/// in-memory store is enough - no DB table, no cross-process concern. A token that's never consumed (the
/// Admin never taps the link) simply expires and is forgotten.</summary>
public sealed class AdminLinkService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int UserId, DateTime ExpiresAtUtc)> _tokens = new();

    public string CreateToken(int userId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        _tokens[token] = (userId, DateTime.UtcNow.Add(TokenLifetime));
        return token;
    }

    /// <summary>Consumes (removes) the token if it exists and hasn't expired - a token only ever works once,
    /// whether or not it succeeds here.</summary>
    public bool TryConsume(string token, out int userId)
    {
        if (_tokens.TryRemove(token, out var entry) && entry.ExpiresAtUtc > DateTime.UtcNow)
        {
            userId = entry.UserId;
            return true;
        }

        userId = 0;
        return false;
    }
}
