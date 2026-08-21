using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace PresentationManager.Application.Services;

/// <summary>Issues and verifies the short-lived 6-digit codes behind "Parolni unutdingizmi?" - delivered to
/// the account's linked Telegram chat (see <see cref="AdminLinkService"/> for how that link is established)
/// as the second factor before letting anyone set a new password. In-memory only, same reasoning as
/// <see cref="AdminLinkService"/>: desktop app and bot share one process, so nothing here needs to survive a
/// restart - a reset abandoned mid-flow just needs requesting again.</summary>
public sealed class PasswordResetService
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<int, (string Code, DateTime ExpiresAtUtc)> _codes = new();
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(ILogger<PasswordResetService> logger)
    {
        _logger = logger;
    }

    public string GenerateCode(int userId)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _codes[userId] = (code, DateTime.UtcNow.Add(CodeLifetime));
        _logger.LogInformation("Parolni tiklash kodi so'raldi: foydalanuvchi {UserId}", userId);
        return code;
    }

    /// <summary>A correct code is consumed (single-use) on success; a wrong one is left in place so the user
    /// can simply retry without needing a brand new code sent.</summary>
    public bool VerifyCode(int userId, string code)
    {
        if (_codes.TryGetValue(userId, out var entry) && entry.ExpiresAtUtc > DateTime.UtcNow && entry.Code == code)
        {
            _codes.TryRemove(userId, out _);
            _logger.LogInformation("Parolni tiklash kodi tasdiqlandi: foydalanuvchi {UserId}", userId);
            return true;
        }

        _logger.LogWarning("Parolni tiklash kodi noto'g'ri yoki muddati o'tgan: foydalanuvchi {UserId}", userId);
        return false;
    }
}
