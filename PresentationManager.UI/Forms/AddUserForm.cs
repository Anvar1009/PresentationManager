using PresentationManager.Domain.Enums;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>SuperAdmin's "+ Foydalanuvchi qo'shish" dialog — the one place new Operator/Admin/SuperAdmin
/// login accounts get created.</summary>
public sealed class AddUserForm : Form
{
    private readonly TextBox _usernameBox;
    private readonly TextBox _passwordBox;
    private readonly TextBox _fullNameBox;
    private readonly ComboBox _roleCombo;

    public string Username => _usernameBox.Text.Trim();
    public string Password => _passwordBox.Text;
    public string FullName => _fullNameBox.Text.Trim();
    public UserRole Role => (UserRole)_roleCombo.SelectedItem!;

    public AddUserForm()
    {
        Text = "Yangi foydalanuvchi";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 280);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _usernameBox = FieldTextBox();
        _passwordBox = FieldTextBox();
        _passwordBox.UseSystemPasswordChar = true;
        _fullNameBox = FieldTextBox();
        _roleCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary
        };
        _roleCombo.Items.AddRange([UserRole.Operator, UserRole.Admin, UserRole.SuperAdmin]);
        _roleCombo.SelectedIndex = 0;

        AddRow(layout, 0, "Login *", _usernameBox);
        AddRow(layout, 1, "Parol *", _passwordBox);
        AddRow(layout, 2, "Ism-familya *", _fullNameBox);
        AddRow(layout, 3, "Rol *", _roleCombo);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelButton = new Button { Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary };
        var saveButton = new Button { Text = "Saqlash", Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.Accent, ForeColor = Color.White };
        saveButton.Click += OnSaveClick;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
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
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(FullName))
        {
            MessageBox.Show(this, "Barcha maydonlar to'ldirilishi shart.", "Tekshiruv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
