using System.Runtime.InteropServices;
using PresentationManager.Application.Services;
using PresentationManager.TelegramBot;

namespace PresentationManager.UI.Forms;

/// <summary>Shared "Botga ulash" flow used by both <see cref="AdminPanelForm"/> and <see cref="SettingsForm"/>
/// (Operator's own copy of the same button, reached via Sozlamalar) - generates a one-time deep-link token
/// (<see cref="AdminLinkService"/>), copies the resulting link to the clipboard, and shows it in a message box
/// so the account can be opened and linked from Telegram.</summary>
internal static class BotLinkHelper
{
    public static void ShowLinkDialog(IWin32Window owner, AdminLinkService adminLinkService, PresentationBotOptions botOptions, int? userId)
    {
        if (userId is not int id)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(botOptions.Username))
        {
            MessageBox.Show(owner,
                "Bot username sozlanmagan (appsettings: TelegramBot:Username). Avval uni sozlang.",
                "Botga ulash", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var token = adminLinkService.CreateToken(id);
        var link = $"https://t.me/{botOptions.Username}?start={token}";

        try
        {
            Clipboard.SetText(link);
        }
        catch (ExternalException)
        {
            // Best-effort - the link is still shown below regardless of whether the clipboard write succeeded.
        }

        MessageBox.Show(owner,
            $"Havola nusxalandi (15 daqiqa amal qiladi):\n\n{link}\n\nUshbu havolani Telegram'da oching - hisobingiz avtomatik ulanadi. Bu \"Parolni unutdingizmi?\" funksiyasi uchun ham kerak bo'ladi.",
            "Botga ulash", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
