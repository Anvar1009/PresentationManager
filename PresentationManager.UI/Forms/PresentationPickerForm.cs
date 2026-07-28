using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.UI.Controls;
using PresentationManager.UI.Localization;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Modal "pick what's next" dialog shown from <see cref="PresentationForm"/>'s control bar once a
/// discussion ends — replaces the old behavior of silently auto-starting whatever came next in queue order,
/// letting the operator instead choose the next presenter themselves.</summary>
/// <remarks>Deliberately does none of the actual session work (finish/select/start) itself — those are all
/// real async DB calls, and running them from a button click handler nested inside this dialog's own
/// <c>ShowDialog</c> message loop was unreliable (the presentation would silently fail to start). Instead
/// this dialog only ever records <see cref="SelectedPresentationId"/> and closes; the caller
/// (<see cref="PresentationForm"/>) does the actual work afterward, once back on its own normal message
/// loop.</remarks>
public sealed class PresentationPickerForm : Form
{
    private readonly PresentationSessionController _session;
    private readonly ListBox _listBox;
    private readonly RoundedButton _startButton;
    private List<Presentation> _items = [];

    /// <summary>Set once the operator picks a row and confirms — null if the dialog was dismissed without
    /// choosing anything.</summary>
    public int? SelectedPresentationId { get; private set; }

    public PresentationPickerForm(PresentationSessionController session)
    {
        _session = session;

        Text = "Keyingi taqdimotni tanlang";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        // Deliberately CenterScreen, not CenterParent — kept even now that the owner passed to ShowDialog is
        // the full-screen PresentationForm itself, since an earlier owner (a thin ~150px strip pinned to the
        // bottom of the screen) once pushed most of this dialog, including the Dock.Bottom BOSHLASH button,
        // off the bottom edge of the screen entirely, which is exactly why picking something and pressing
        // Boshlash appeared to do nothing: the button wasn't reachable, on-screen, to actually click.
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 480);

        var header = new Label
        {
            Text = "Keyingi taqdimotchini tanlang va boshlang:",
            Dock = DockStyle.Top,
            Height = 32,
            ForeColor = AppColors.TextSecondary,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.PanelAlt,
            ForeColor = AppColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 11)
        };
        _listBox.SelectedIndexChanged += (_, _) => _startButton.Enabled = _listBox.SelectedIndex >= 0;
        _listBox.DoubleClick += (_, _) => TryAccept();

        _startButton = new RoundedButton
        {
            Text = "BOSHLASH",
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = AppColors.Success,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            Enabled = false
        };
        _startButton.Click += (_, _) => TryAccept();

        var padded = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        padded.Controls.Add(_listBox);
        padded.Controls.Add(header);

        Controls.Add(padded);
        Controls.Add(_startButton);

        Load += (_, _) => PopulateList();
    }

    private void PopulateList()
    {
        _items = _session.Queue.ToList();
        _listBox.Items.Clear();
        foreach (var p in _items)
        {
            _listBox.Items.Add($"{p.OrderNumber + 1}. {p.FullName} - {p.Title}  [{UzbekText.StatusLabel(p.Status)}]");
        }
    }

    private void TryAccept()
    {
        var index = _listBox.SelectedIndex;
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        SelectedPresentationId = _items[index].Id;

        // Setting DialogResult alone closes a form shown via ShowDialog (see PresentationEditForm's
        // Saqlash button for the same established pattern elsewhere in this app) — an extra explicit
        // Close() call here raced with that and was the actual reason picking something and pressing
        // Boshlash silently did nothing: ShowDialog's caller never reliably saw DialogResult.OK.
        DialogResult = DialogResult.OK;
    }
}
