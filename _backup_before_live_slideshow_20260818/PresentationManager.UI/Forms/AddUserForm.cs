using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>SuperAdmin's "+ Foydalanuvchi qo'shish" dialog — the one place new Operator/Admin/SuperAdmin login
/// accounts get created. Nothing is typed by hand: the person must already have registered through the
/// Telegram bot (as a <see cref="Presenter"/>), so this only ever asks which of them to promote and to what
/// role — see <see cref="Application.Services.UserService.CreateFromPresenterAsync"/> for how the actual
/// login/password get generated from that choice.</summary>
public sealed class AddUserForm : Form
{
    private readonly ComboBox _presenterCombo;
    private readonly ComboBox _roleCombo;

    public Presenter SelectedPresenter => ((PresenterOption)_presenterCombo.SelectedItem!).Presenter;
    public UserRole Role => (UserRole)_roleCombo.SelectedItem!;

    public AddUserForm(List<Presenter> availablePresenters)
    {
        Text = "Yangi foydalanuvchi";
        BackColor = LightColors.Background;
        ForeColor = LightColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 200);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _presenterCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary
        };
        _presenterCombo.Items.AddRange(availablePresenters.Select(p => (object)new PresenterOption(p)).ToArray());
        _presenterCombo.SelectedIndex = 0;

        _roleCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = LightColors.PanelAlt,
            ForeColor = LightColors.TextPrimary
        };
        _roleCombo.Items.AddRange([UserRole.Operator, UserRole.Admin, UserRole.SuperAdmin, UserRole.OrderOperator, UserRole.Judge]);
        _roleCombo.SelectedIndex = 0;

        AddRow(layout, 0, "Taqdimotchi *", _presenterCombo);
        AddRow(layout, 1, "Rol *", _roleCombo);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelButton = new Button { Text = "Bekor qilish", DialogResult = DialogResult.Cancel, Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.PanelAlt, ForeColor = LightColors.TextPrimary };
        var saveButton = new Button { Text = "Saqlash", Width = 90, FlatStyle = FlatStyle.Flat, BackColor = LightColors.Accent, ForeColor = Color.White };
        saveButton.Click += OnSaveClick;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(buttonPanel, 0, 2);
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

    private void OnSaveClick(object? sender, EventArgs e)
    {
        if (_presenterCombo.SelectedItem is null)
        {
            MessageBox.Show(this, "Taqdimotchini tanlang.", "Tekshiruv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
    }

    /// <summary>Wraps a <see cref="Presenter"/> only so the combo box shows something more useful than its
    /// type name — <see cref="ComboBox"/> renders items via <c>ToString()</c> and <see cref="Presenter"/>
    /// itself has no reason to own a display format.</summary>
    private sealed record PresenterOption(Presenter Presenter)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Presenter.PhoneNumber)
            ? Presenter.FullName
            : $"{Presenter.FullName} — {Presenter.PhoneNumber}";
    }
}
