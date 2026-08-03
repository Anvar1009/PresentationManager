using PresentationManager.Domain.Entities;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>SuperAdmin's "Login/parolni tiklash" dialog — lets an existing account's login be changed and,
/// optionally, its password reset. This is the account-recovery path for a user who forgot their
/// credentials: with the account still visible in the Users list, a SuperAdmin edits it back to something
/// the user can log in with, instead of the user having to remember anything themselves.</summary>
public sealed class EditUserForm : Form
{
    private readonly TextBox _usernameBox;
    private readonly TextBox _passwordBox;

    public string Username => _usernameBox.Text.Trim();

    /// <summary>Empty means "keep the current password" — <see cref="Application.Services.UserService.EditUserAsync"/>
    /// only resets it when this is non-blank.</summary>
    public string NewPassword => _passwordBox.Text;

    public EditUserForm(User user)
    {
        Text = $"Foydalanuvchini tahrirlash — {user.FullName}";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 230);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _usernameBox = FieldTextBox();
        _usernameBox.Text = user.Username;
        _passwordBox = FieldTextBox();
        _passwordBox.UseSystemPasswordChar = true;

        AddRow(layout, 0, "Login *", _usernameBox);
        AddRow(layout, 1, "Yangi parol", _passwordBox);

        var hintLabel = new Label
        {
            Text = "Bo'sh qoldirsangiz, joriy parol o'zgarmaydi.",
            Dock = DockStyle.Fill,
            ForeColor = LightColors.TextSecondary,
            Font = new Font("Segoe UI", 8.5f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.Controls.Add(hintLabel, 1, 2);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelButton = new Button { Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary };
        var saveButton = new Button { Text = "Saqlash", Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.Accent, ForeColor = Color.White };
        saveButton.Click += OnSaveClick;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(buttonPanel, 0, 3);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string labelText, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(new Label { Text = labelText, ForeColor = LightColors.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static TextBox FieldTextBox() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = LightColors.PanelAlt,
        ForeColor = LightColors.TextPrimary,
        BorderStyle = BorderStyle.FixedSingle
    };

    private void OnSaveClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            MessageBox.Show(this, "Login bo'sh bo'lishi mumkin emas.", "Tekshiruv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (NewPassword.Length > 0 && NewPassword.Length < 6)
        {
            MessageBox.Show(this, "Parol kamida 6 ta belgidan iborat bo'lishi kerak.", "Tekshiruv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
