using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>SuperAdmin's "Login/parolni tiklash" dialog — lets an existing account's login and role be
/// changed and, optionally, its password reset. Covers two different SuperAdmin actions that both start from
/// picking a row in the Foydalanuvchilar grid: account recovery (login/password) for a user who forgot their
/// credentials, and promoting/demoting a role — both by selecting, never retyping the account's identity.</summary>
public sealed class EditUserForm : Form
{
    private readonly TextBox _usernameBox;
    private readonly TextBox _passwordBox;
    private readonly ComboBox _roleCombo;

    public string Username => _usernameBox.Text.Trim();

    /// <summary>Empty means "keep the current password" — <see cref="Application.Services.UserService.EditUserAsync"/>
    /// only resets it when this is non-blank.</summary>
    public string NewPassword => _passwordBox.Text;

    public UserRole Role => (UserRole)_roleCombo.SelectedItem!;

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
        ClientSize = new Size(380, 270);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _usernameBox = FieldTextBox();
        _usernameBox.Text = user.Username;
        _passwordBox = FieldTextBox();
        _passwordBox.UseSystemPasswordChar = true;
        _roleCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary
        };
        _roleCombo.Items.AddRange([UserRole.Operator, UserRole.Admin, UserRole.SuperAdmin]);
        _roleCombo.SelectedItem = user.Role;

        AddRow(layout, 0, "Login *", _usernameBox);
        AddRow(layout, 1, "Yangi parol", _passwordBox);
        AddRow(layout, 2, "Rol *", _roleCombo);

        var hintLabel = new Label
        {
            Text = "Parolni bo'sh qoldirsangiz, joriy parol o'zgarmaydi.",
            Dock = DockStyle.Fill,
            ForeColor = LightColors.TextSecondary,
            Font = new Font("Segoe UI", 8.5f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.Controls.Add(hintLabel, 1, 3);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelButton = new Button { Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary };
        var saveButton = new Button { Text = "Saqlash", Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.Accent, ForeColor = Color.White };
        saveButton.Click += OnSaveClick;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(buttonPanel, 0, 4);
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
