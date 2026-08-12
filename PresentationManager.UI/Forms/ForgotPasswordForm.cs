using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.UI.Controls;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Opened from <see cref="LoginForm"/>'s "Parolni unutdingizmi?" link. Three steps, one panel each
/// (only one <see cref="Panel.Visible"/> at a time): enter the Telegram @username the account was linked with
/// (see <c>AdminPanelForm</c>/<c>SettingsForm</c>'s "Botga ulash") → enter the 6-digit code the bot just sent
/// to that chat (<see cref="PasswordResetService"/>) → set a new password. An account that was never linked
/// has no chat to deliver a code to, so step 1 fails outright for it - there's no other recovery path today.</summary>
public sealed class ForgotPasswordForm : Form
{
    private readonly UserService _userService;
    private readonly PasswordResetService _passwordResetService;
    private readonly ITelegramSender _telegramSender;

    private User? _targetUser;

    private readonly Panel _usernameStep;
    private readonly Panel _codeStep;
    private readonly Panel _passwordStep;
    private readonly Label _statusLabel;

    private readonly TextBox _usernameBox;
    private readonly TextBox _codeBox;
    private readonly TextBox _newPasswordBox;
    private readonly TextBox _confirmPasswordBox;

    public ForgotPasswordForm(UserService userService, PasswordResetService passwordResetService, ITelegramSender telegramSender)
    {
        _userService = userService;
        _passwordResetService = passwordResetService;
        _telegramSender = telegramSender;

        Text = "Parolni tiklash";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 330);

        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(24, 0, 24, 12),
            ForeColor = AppColors.Danger,
            Font = new Font("Segoe UI", 8.5f),
            Text = string.Empty
        };

        _usernameStep = BuildUsernameStep(out _usernameBox);
        _codeStep = BuildCodeStep(out _codeBox);
        _passwordStep = BuildPasswordStep(out _newPasswordBox, out _confirmPasswordBox);
        _codeStep.Visible = false;
        _passwordStep.Visible = false;

        Controls.Add(_passwordStep);
        Controls.Add(_codeStep);
        Controls.Add(_usernameStep);
        Controls.Add(_statusLabel);
    }

    private Panel BuildUsernameStep(out TextBox usernameBox)
    {
        var step = new Panel { Dock = DockStyle.Fill };

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(24, 20, 24, 0),
            Text = "Hisobingizga ulangan Telegram username'ni kiriting - tasdiqlash kodi shu akkauntga yuboriladi.",
            ForeColor = AppColors.TextSecondary,
            Font = new Font("Segoe UI", 9f)
        };

        usernameBox = DarkTextBox();
        usernameBox.PlaceholderText = "username (@ belgisisiz)";
        var usernameWrap = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(24, 4, 24, 0) };
        usernameWrap.Controls.Add(usernameBox);

        var sendButton = PrimaryButton("Kod yuborish");
        sendButton.Click += OnSendCodeClick;
        var sendWrap = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(24, 20, 24, 0) };
        sendWrap.Controls.Add(sendButton);

        step.Controls.Add(sendWrap);
        step.Controls.Add(usernameWrap);
        step.Controls.Add(label);

        // No Form.AcceptButton here - it can't move between steps, so Enter is wired per-textbox instead.
        var capturedBox = usernameBox;
        capturedBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OnSendCodeClick(sendButton, EventArgs.Empty); };

        return step;
    }

    private Panel BuildCodeStep(out TextBox codeBox)
    {
        var step = new Panel { Dock = DockStyle.Fill };

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(24, 20, 24, 0),
            Text = "Telegram'ga yuborilgan 6 xonali kodni kiriting:",
            ForeColor = AppColors.TextSecondary,
            Font = new Font("Segoe UI", 9f)
        };

        codeBox = DarkTextBox();
        codeBox.PlaceholderText = "000000";
        codeBox.MaxLength = 6;
        var codeWrap = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(24, 4, 24, 0) };
        codeWrap.Controls.Add(codeBox);

        var verifyButton = PrimaryButton("Tasdiqlash");
        verifyButton.Click += OnVerifyCodeClick;
        var verifyWrap = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(24, 20, 24, 0) };
        verifyWrap.Controls.Add(verifyButton);

        step.Controls.Add(verifyWrap);
        step.Controls.Add(codeWrap);
        step.Controls.Add(label);

        var capturedBox = codeBox;
        capturedBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OnVerifyCodeClick(verifyButton, EventArgs.Empty); };

        return step;
    }

    private Panel BuildPasswordStep(out TextBox newPasswordBox, out TextBox confirmPasswordBox)
    {
        var step = new Panel { Dock = DockStyle.Fill };

        var newLabel = new Label { Text = "Yangi parol", Dock = DockStyle.Top, Height = 26, Padding = new Padding(24, 20, 24, 0), ForeColor = AppColors.TextSecondary, Font = new Font("Segoe UI", 8.5f) };
        newPasswordBox = DarkTextBox();
        newPasswordBox.UseSystemPasswordChar = true;
        var newWrap = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(24, 4, 24, 0) };
        newWrap.Controls.Add(newPasswordBox);

        var confirmLabel = new Label { Text = "Parolni takrorlang", Dock = DockStyle.Top, Height = 26, Padding = new Padding(24, 12, 24, 0), ForeColor = AppColors.TextSecondary, Font = new Font("Segoe UI", 8.5f) };
        confirmPasswordBox = DarkTextBox();
        confirmPasswordBox.UseSystemPasswordChar = true;
        var confirmWrap = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(24, 4, 24, 0) };
        confirmWrap.Controls.Add(confirmPasswordBox);

        var saveButton = PrimaryButton("Saqlash");
        saveButton.Click += OnSavePasswordClick;
        var saveWrap = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(24, 20, 24, 0) };
        saveWrap.Controls.Add(saveButton);

        step.Controls.Add(saveWrap);
        step.Controls.Add(confirmWrap);
        step.Controls.Add(confirmLabel);
        step.Controls.Add(newWrap);
        step.Controls.Add(newLabel);

        var capturedConfirmBox = confirmPasswordBox;
        capturedConfirmBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OnSavePasswordClick(saveButton, EventArgs.Empty); };

        return step;
    }

    private static TextBox DarkTextBox() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = AppColors.PanelAlt,
        ForeColor = AppColors.TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 10.5f)
    };

    private static RoundedButton PrimaryButton(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        BackColor = AppColors.Accent,
        Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
    };

    private async void OnSendCodeClick(object? sender, EventArgs e)
    {
        _statusLabel.Text = string.Empty;
        var username = _usernameBox.Text.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(username))
        {
            _statusLabel.Text = "Telegram username'ni kiriting.";
            return;
        }

        var user = await _userService.GetByTelegramUsernameAsync(username);
        if (user is null || user.TelegramChatId is not { } chatId)
        {
            _statusLabel.Text = "Bunday hisob topilmadi yoki Telegram bilan ulanmagan. Administratorga murojaat qiling.";
            return;
        }

        var code = _passwordResetService.GenerateCode(user.Id);
        var sent = await _telegramSender.TrySendMessageAsync(chatId, $"🔐 Parolni tiklash kodi: {code}\n\nKod 10 daqiqa amal qiladi.");
        if (!sent)
        {
            _statusLabel.Text = "Kod yuborilmadi - bot o'chiq yoki Telegram bilan bog'lanib bo'lmadi.";
            return;
        }

        _targetUser = user;
        _usernameStep.Visible = false;
        _codeStep.Visible = true;
    }

    private void OnVerifyCodeClick(object? sender, EventArgs e)
    {
        _statusLabel.Text = string.Empty;
        if (_targetUser is null)
        {
            return;
        }

        var code = _codeBox.Text.Trim();
        if (!_passwordResetService.VerifyCode(_targetUser.Id, code))
        {
            _statusLabel.Text = "Kod noto'g'ri yoki muddati o'tgan.";
            return;
        }

        _codeStep.Visible = false;
        _passwordStep.Visible = true;
    }

    private async void OnSavePasswordClick(object? sender, EventArgs e)
    {
        _statusLabel.Text = string.Empty;
        if (_targetUser is null)
        {
            return;
        }

        if (_newPasswordBox.Text != _confirmPasswordBox.Text)
        {
            _statusLabel.Text = "Parollar mos emas.";
            return;
        }

        try
        {
            await _userService.ResetPasswordAsync(_targetUser.Id, _newPasswordBox.Text);
            MessageBox.Show(this, "Parol muvaffaqiyatli o'zgartirildi. Endi yangi parol bilan kiring.", "Muvaffaqiyatli", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
        }
    }
}
