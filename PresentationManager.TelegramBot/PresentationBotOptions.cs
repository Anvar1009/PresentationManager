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

    /// <summary>Base URL of the Judge web platform (PresentationManager.API's Account/Judge MVC pages, e.g.
    /// "http://192.168.5.140:5000") - included in the message a Judge chat gets instead of the old in-chat
    /// scoring flow (see <c>PresentationBotHostedService.ShowJudgeWebRedirectAsync</c>). Empty is handled
    /// gracefully (the message just omits the link) since, like <see cref="Token"/>/<see cref="Username"/>,
    /// this is a per-deployment value that isn't checked into source control.</summary>
    public string JudgeWebBaseUrl { get; set; } = string.Empty;
}
