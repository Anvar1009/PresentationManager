using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PresentationManager.Application.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace PresentationManager.TelegramBot;

/// <summary>Sends Telegram messages outside of <see cref="PresentationBotHostedService"/>'s own
/// receive-and-respond loop - e.g. a password-reset code, or a judge-assignment push. Deliberately its own
/// independent <see cref="ITelegramBotClient"/> rather than sharing the hosted service's: unlike the
/// long-polling <c>getUpdates</c> loop (which Telegram only lets one consumer hold per bot token at a time),
/// <c>sendMessage</c> has no such restriction, so every process that knows the token - every desktop client,
/// and the bot's own worker process - can send independently with no coordination needed.
/// Only ever constructed server-side now (PresentationManager.API, PresentationManager.BotService) - see
/// <see cref="ITelegramSender"/> for the text-only contract PresentationManager.UI depends on instead,
/// relayed over HTTP by PresentationManager.API.Controllers.NotificationsController so the bot token itself
/// never has to live on a desktop machine.</summary>
public sealed class TelegramNotifier : ITelegramSender
{
    private readonly ITelegramBotClient? _botClient;
    private readonly ILogger<TelegramNotifier> _logger;

    /// <summary>Null when no token is configured - matches <see cref="PresentationBotHostedService"/>'s own
    /// "stay quietly off" behavior instead of requiring the token just to run the desktop app.</summary>
    public TelegramNotifier(IOptions<PresentationBotOptions> options, ILogger<TelegramNotifier> logger)
    {
        _botClient = string.IsNullOrWhiteSpace(options.Value.Token) ? null : new TelegramBotClient(options.Value.Token);
        _logger = logger;
    }

    /// <summary>Best-effort: returns false rather than throwing if the bot is off or the send itself fails
    /// (e.g. the account blocked the bot), letting the caller show its own error instead of crashing.</summary>
    public async Task<bool> TrySendMessageAsync(long chatId, string text, ReplyMarkup? replyMarkup = null, CancellationToken ct = default)
    {
        if (_botClient is not { } botClient)
        {
            _logger.LogWarning("Telegram xabar yuborilmadi - bot tokeni sozlanmagan (chat {ChatId}).", chatId);
            return false;
        }

        try
        {
            await botClient.SendMessage(chatId, text, replyMarkup: replyMarkup, cancellationToken: ct);
            _logger.LogInformation("Telegram xabar yuborildi: chat {ChatId}", chatId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram xabar yuborishda xatolik: chat {ChatId}", chatId);
            return false;
        }
    }

    /// <summary>Explicit (not a plain overload) so ITelegramSender-typed callers can't accidentally omit
    /// the ReplyMarkup this class's own callers still rely on (JudgeAssignmentNotifier, ...) - it just
    /// delegates to the real method above with none.</summary>
    Task<bool> ITelegramSender.TrySendMessageAsync(long chatId, string text, CancellationToken ct) =>
        TrySendMessageAsync(chatId, text, replyMarkup: null, ct);
}
