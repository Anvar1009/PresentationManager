namespace PresentationManager.Application.Interfaces;

/// <summary>Minimal, text-only relay for the handful of outbound Telegram messages
/// PresentationManager.UI itself still triggers directly (password reset codes, SuperAdmin-issued
/// credentials) - kept deliberately smaller than PresentationManager.TelegramBot.TelegramNotifier (no
/// ReplyMarkup) so this interface, and everything that depends on it, never needs a reference to
/// Telegram.Bot's types. The bot token itself now lives only on the server - see
/// PresentationManager.ApiClient.HttpTelegramSender and PresentationManager.API.Controllers.
/// NotificationsController.</summary>
public interface ITelegramSender
{
    /// <summary>Best-effort - false (not an exception) on a missing/blocked chat or an unconfigured bot,
    /// matching TelegramNotifier's own existing contract.</summary>
    Task<bool> TrySendMessageAsync(long chatId, string text, CancellationToken ct = default);
}
