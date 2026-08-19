using PresentationManager.UI.Controls;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>Tiny modal shown once the slide is already up on the projector screen but before the timer
/// starts ticking — separates "the slide is now visible" from "the clock is running" into two distinct
/// operator actions, asked only once there's actually something on screen to confirm against. Closes itself
/// the instant its one button is pressed.</summary>
public sealed class StartPresentationGateForm : Form
{
    public StartPresentationGateForm(string fullName, string title)
    {
        Text = "Taymerni boshlash";
        BackColor = AppColors.Background;
        ForeColor = AppColors.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        // Not CenterParent — a lesson from PresentationPickerForm: centering relative to an owner that's not
        // yet shown at all (first start of the day, from AdminForm) can park this dialog somewhere the
        // operator can't actually see or reach. Also deliberately not CenterScreen either, despite looking
        // like the obvious fix for that: with no owner, WinForms centers a CenterScreen form on whichever
        // monitor the mouse cursor happens to be over at that instant, not reliably the operator's own
        // screen - on a laptop+projector setup that meant this gate would sometimes pop up on the shared
        // projector screen instead of in front of the operator, depending on where the mouse last was. Manual
        // positioning onto Screen.PrimaryScreen (see Load below) is what PresentationForm's own
        // PositionOnTargetMonitor treats as "the operator's screen" too, so this is always where AdminForm
        // itself lives, never the shared one.
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(440, 240);

        Load += (_, _) =>
        {
            var operatorScreen = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
            Location = new Point(
                operatorScreen.X + (operatorScreen.Width - Width) / 2,
                operatorScreen.Y + (operatorScreen.Height - Height) / 2);
        };

        var nameLabel = new Label
        {
            Text = fullName,
            Dock = DockStyle.Top,
            Height = 44,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI", 12, FontStyle.Italic),
            ForeColor = AppColors.Accent,
            TextAlign = ContentAlignment.MiddleCenter
        };
        var hint = new Label
        {
            Text = "Taqdimot ekranga chiqarildi. Taymerni hozir boshlaysizmi?",
            Dock = DockStyle.Top,
            Height = 48,
            ForeColor = AppColors.TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9f)
        };

        var startButton = new RoundedButton
        {
            Text = "TAYMERNI BOSHLASH",
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = AppColors.Success,
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        };
        // Setting DialogResult alone closes a form shown via ShowDialog (same pattern as
        // PresentationEditForm's Saqlash button) — no explicit Close() needed, and none added.
        startButton.Click += (_, _) => DialogResult = DialogResult.OK;

        Controls.Add(startButton);
        Controls.Add(hint);
        Controls.Add(titleLabel);
        Controls.Add(nameLabel);

        AcceptButton = startButton;
    }
}
