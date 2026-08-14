using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Shared "who's logged in / log out" menu content for all three role dashboards (AdminForm,
/// AdminPanelForm, SuperAdminPanelForm). There's no in-process way back to LoginForm once a role dashboard is
/// showing - Program.cs calls <c>WinFormsApp.Run(mainForm)</c> exactly once and the process exits when that
/// form closes - so "Chiqish" restarts the whole app back to the login screen rather than swapping forms.</summary>
internal static class UserMenuHelper
{
    /// <summary>Read-only login/role info, a "Login/parolni o'zgartirish" self-service action, and
    /// "Chiqish" - built fresh on every call (each caller owns its own item instances) so the same content
    /// can populate either a MenuStrip item's DropDownItems or a standalone ContextMenuStrip's Items. Shows
    /// the account's login and its raw <see cref="Domain.Enums.UserRole"/> only - not
    /// <see cref="User.FullName"/>, which is free-text data entered when the account was created and isn't
    /// guaranteed to match who's actually logged in.</summary>
    /// <param name="onProfileUpdated">Invoked after a successful login/password change so the caller can
    /// refresh whatever else displays <paramref name="user"/>'s username (e.g. the account button's own
    /// text) - <paramref name="user"/> itself is mutated in place first, so re-reading its properties there
    /// is enough, no need to re-fetch.</param>
    public static ToolStripItem[] BuildItems(User user, UserService userService, IWin32Window owner, Action onProfileUpdated) =>
    [
        new ToolStripMenuItem($"{user.Username} · {user.Role}")
        {
            Enabled = false,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = LightColors.TextPrimary
        },
        new ToolStripSeparator(),
        CreateEditProfileItem(user, userService, owner, onProfileUpdated),
        new ToolStripSeparator(),
        CreateLogoutItem(owner)
    ];

    private static ToolStripMenuItem CreateEditProfileItem(User user, UserService userService, IWin32Window owner, Action onProfileUpdated)
    {
        var item = new ToolStripMenuItem("✏️ Login/parolni o'zgartirish") { ForeColor = LightColors.TextPrimary };
        item.Click += async (_, _) =>
        {
            using var dialog = new EditOwnProfileForm(user);
            if (dialog.ShowDialog(owner) != DialogResult.OK)
            {
                return;
            }

            try
            {
                await userService.ChangeOwnCredentialsAsync(
                    user.Id, dialog.Username, string.IsNullOrEmpty(dialog.NewPassword) ? null : dialog.NewPassword);
                user.Username = dialog.Username;
                onProfileUpdated();
                MessageBox.Show(owner, "Profil yangilandi.", "Muvaffaqiyatli", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, ex.Message, "Profilni tahrirlashda xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        return item;
    }

    private static ToolStripMenuItem CreateLogoutItem(IWin32Window owner)
    {
        var item = new ToolStripMenuItem("🚪 Chiqish") { ForeColor = LightColors.Danger };
        item.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(owner,
                "Tizimdan chiqishni xohlaysizmi?",
                "Chiqish", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm == DialogResult.Yes)
            {
                WinFormsApp.Restart();
            }
        };
        return item;
    }
}
