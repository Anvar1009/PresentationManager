using PresentationManager.Domain.Entities;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Self-service "change my login/password" dialog, opened from the account menu
/// (<see cref="UserMenuHelper"/>) by any logged-in role. Unlike <see cref="EditUserForm"/> (SuperAdmin's
/// dialog for editing any account's name/login/role), this only ever targets the caller's own account and
/// exposes just Login/Yangi parol - no FullName, no Rol.</summary>
public sealed class EditOwnProfileForm : Form
{
    private readonly TextBox _usernameBox;
    private readonly TextBox _passwordBox;

    public string Username => _usernameBox.Text.Trim();

    /// <summary>Empty means "keep the current password" - same convention as <see cref="EditUserForm.NewPassword"/>.</summary>
    public string NewPassword => _passwordBox.Text;

    public EditOwnProfileForm(User user)
    {
        Text = "Profilni tahrirlash";
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
            Text = "Parolni bo'sh qoldirsangiz, joriy parol o'zgarmaydi.",
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
