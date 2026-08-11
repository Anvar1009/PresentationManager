using System.Diagnostics;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace PresentationManager.TelegramBot;

/// <summary>Sends Telegram messages outside of <see cref="PresentationBotHostedService"/>'s own
/// receive-and-respond loop - e.g. a password-reset code, or a judge-assignment push. Deliberately its own
/// independent <see cref="ITelegramBotClient"/> rather than sharing the hosted service's: unlike the
/// long-polling <c>getUpdates</c> loop (which Telegram only lets one consumer hold per bot token at a time),
/// <c>sendMessage</c> has no such restriction, so every process that knows the token - every desktop client,
/// and the bot's own worker process - can send independently with no coordination needed.</summary>
public sealed class TelegramNotifier
{
    private readonly ITelegramBotClient? _botClient;

    /// <summary>Null when no token is configured - matches <see cref="PresentationBotHostedService"/>'s own
    /// "stay quietly off" behavior instead of requiring the token just to run the desktop app.</summary>
    public TelegramNotifier(IOptions<PresentationBotOptions> options)
    {
        _botClient = string.IsNullOrWhiteSpace(options.Value.Token) ? null : new TelegramBotClient(options.Value.Token);
    }

    /// <summary>Best-effort: returns false rather than throwing if the bot is off or the send itself fails
    /// (e.g. the account blocked the bot), letting the caller show its own error instead of crashing.</summary>
    public async Task<bool> TrySendMessageAsync(long chatId, string text, ReplyMarkup? replyMarkup = null, CancellationToken ct = default)
    {
        if (_botClient is not { } botClient)
        {
            return false;
        }

        try
        {
            await botClient.SendMessage(chatId, text, replyMarkup: replyMarkup, cancellationToken: ct);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to send message to chat {chatId}: {ex}");
            return false;
        }
    }
}
