using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.TelegramBot;
using PresentationManager.UI.Controls;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>The very first thing shown on launch — only Operator/Admin/SuperAdmin log in here (Presenters
/// and Judges never touch the desktop app, only the Telegram bot). On success, <see cref="AuthenticatedUser"/>
/// tells <c>Program.cs</c> which of the three role-specific main forms to run. "Parolni unutdingizmi?" opens
/// <see cref="ForgotPasswordForm"/> - a separate modal so this form's own layout stays simple.</summary>
public sealed class LoginForm : Form
{
    private readonly UserService _userService;
    private readonly PasswordResetService _passwordResetService;
    private readonly PresentationBotHostedService _botHostedService;
    private readonly TextBox _usernameBox;
    private readonly TextBox _passwordBox;
    private readonly Label _errorLabel;

    public User? AuthenticatedUser { get; private set; }

    public LoginForm(UserService userService, PasswordResetService passwordResetService, PresentationBotHostedService botHostedService)
    {
        _userService = userService;
        _passwordResetService = passwordResetService;
        _botHostedService = botHostedService;

        Text = "Kirish";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 290);

        var title = new Label
        {
            Text = "Taqdimotlar Boshqaruvi",
            Dock = DockStyle.Top,
            Height = 44,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = AppColors.TextPrimary
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(24, 8, 24, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _usernameBox = DarkTextBox();
        _passwordBox = DarkTextBox();
        _passwordBox.UseSystemPasswordChar = true;

        AddRow(layout, 0, "Login", _usernameBox);
        AddRow(layout, 1, "Parol", _passwordBox);

        _errorLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new Padding(24, 4, 24, 0),
            ForeColor = AppColors.Danger,
            Font = new Font("Segoe UI", 8.5f),
            Text = string.Empty
        };

        var loginButton = new RoundedButton
        {
            Text = "Kirish",
            Dock = DockStyle.Top,
            Height = 46,
            Margin = new Padding(24, 12, 24, 0),
            BackColor = AppColors.Accent,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        loginButton.Click += OnLoginClick;
        var buttonWrap = new Panel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(24, 12, 24, 0) };
        buttonWrap.Controls.Add(loginButton);

        var forgotPasswordLink = new LinkLabel
        {
            Text = "Parolni unutdingizmi?",
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(24, 6, 24, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            LinkColor = AppColors.Accent,
            ActiveLinkColor = AppColors.Accent,
            VisitedLinkColor = AppColors.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Font = new Font("Segoe UI", 8.5f)
        };
        forgotPasswordLink.LinkClicked += (_, _) => OnForgotPasswordClick();

        Controls.Add(buttonWrap);
        Controls.Add(_errorLabel);
        Controls.Add(forgotPasswordLink);
        Controls.Add(layout);
        Controls.Add(title);

        AcceptButton = loginButton;
        _passwordBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OnLoginClick(this, EventArgs.Empty);
            }
        };
    }

    private static void AddRow(TableLayoutPanel layout, int row, string labelText, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(new Label { Text = labelText, ForeColor = AppColors.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static TextBox DarkTextBox() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = AppColors.PanelAlt,
        ForeColor = AppColors.TextPrimary,
        BorderStyle = BorderStyle.FixedSingle
    };

    private async void OnLoginClick(object? sender, EventArgs e)
    {
        _errorLabel.Text = string.Empty;

        var username = _usernameBox.Text.Trim();
        var password = _passwordBox.Text;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _errorLabel.Text = "Login va parolni kiriting.";
            return;
        }

        try
        {
            var user = await _userService.ValidateLoginAsync(username, password);
            if (user is null)
            {
                _errorLabel.Text = "Login yoki parol noto'g'ri.";
                _passwordBox.SelectAll();
                _passwordBox.Focus();
                return;
            }

            AuthenticatedUser = user;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _errorLabel.Text = ex.Message;
        }
    }

    private void OnForgotPasswordClick()
    {
        using var dialog = new ForgotPasswordForm(_userService, _passwordResetService, _botHostedService);
        dialog.ShowDialog(this);
    }
}
