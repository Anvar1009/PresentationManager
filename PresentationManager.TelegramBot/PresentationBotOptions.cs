namespace PresentationManager.TelegramBot;

/// <summary>Bound from the "TelegramBot" configuration section. An empty <see cref="Token"/> means the bot
/// is simply left switched off — <see cref="PresentationBotHostedService"/> stays idle instead of failing
/// the whole app, since the token is a per-deployment secret that isn't checked into source control.</summary>
public sealed class PresentationBotOptions
{
    public string Token { get; set; } = string.Empty;

    /// <summary>The bot's @username (without the @), used only to build the "Botga ulash" deep link
    /// (<c>https://t.me/{Username}?start={token}</c>) shown by <c>AdminPanelForm</c> - not required for the
    /// bot itself to function.</summary>
    public string Username { get; set; } = string.Empty;
}
