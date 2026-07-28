using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Enums;
using PresentationManager.UI.Controls;
using PresentationManager.UI.SlideDisplay;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>
/// The projector-facing screen. Starting a presentation is still triggered from <see cref="AdminForm"/>,
/// but once running, the live timer and "Keyingi" (Next) button stay visible here as small floating
/// controls over the slide — no separate backing bar/panel. Fullscreen on the second monitor when one
/// exists, otherwise fullscreen on the primary monitor.
/// </summary>
public sealed class PresentationForm : Form
{
    /// <summary>Starting point for <see cref="PositionDiscussionHeader"/>'s fit-to-width shrinking — the
    /// letter-spaced headline (see <see cref="SpacedOut"/>) at this size can be wider than the window on
    /// narrower monitors or the non-fullscreen dev window, which silently clipped trailing characters
    /// (e.g. the final "R" in "javoblar") since Label draws past its bounds without wrapping.</summary>
    private const int DiscussionHeaderMaxFontSize = 60;
    private const int DiscussionHeaderMinFontSize = 20;

    /// <summary>Final stretch of either timer (10, 9, 8, ... 1) where every second gets its own short beep
    /// plus a synchronized gold/red blink — see <see cref="HandleWarningState"/>. The expiry alarm at 0 is
    /// separate — see <see cref="PlayFinalAlarm"/>.</summary>
    private const int WarningThresholdSeconds = 10;

    private readonly PresentationSessionController _session;
    private readonly PdfSlideDisplayService _pdfDisplayService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IAlarmSoundService _alarmSoundService;

    private readonly Label _timerLabel;
    private readonly Panel _discussionSpacer;
    private readonly Label _discussionHeader;
    private readonly Panel _discussionAccentLine;
    private readonly Panel _discussionBottomGap;
    private readonly Button _closeButton;

    /// <summary>Small always-visible countdown shown over the slide while it's covering the big centered
    /// <see cref="_timerLabel"/> — plain floating controls (like <see cref="_closeButton"/>), not a backing
    /// panel/bar, so nothing but the digits and the button themselves are ever on screen.</summary>
    private readonly TransparentOverlayLabel _miniTimerLabel;
    private readonly RoundedButton _controlButton;

    /// <summary>The embedded WebView2 PDF viewer (used for both real PDFs and PPTX-converted-to-PDF) is
    /// hosted inside this panel.</summary>
    public Panel ContentHost { get; }

    private readonly System.Windows.Forms.Timer _blinkTimer;
    private bool _blinkVisible = true;

    private bool _slideOpen;

    /// <summary>Which presentation's file is currently loaded in <see cref="_pdfDisplayService"/> — needed
    /// alongside <see cref="_slideOpen"/> because NextPresenterAsync can now go straight from one
    /// presentation's Discussion to the next one's Running with no Ready/closed state in between (it chains
    /// automatically). Without tracking the Id, Running's "_slideOpen ? Show : Open" check couldn't tell
    /// "still showing the previous file" apart from "showing this one already" and just re-showed the old
    /// file instead of opening the new one.</summary>
    private int? _openPresentationId;

    public PresentationForm(
        PresentationSessionController session,
        IFileStorageService fileStorageService,
        ISettingsRepository settingsRepository,
        IAlarmSoundService alarmSoundService)
    {
        _session = session;
        _fileStorageService = fileStorageService;
        _settingsRepository = settingsRepository;
        _alarmSoundService = alarmSoundService;

        Text = "Namoyish Ekrani";
        BackColor = AppColors.Background;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        // FormBorderStyle/TopMost/ShowInTaskbar are set dynamically in PositionOnTargetMonitor, based on
        // whether a real second monitor exists — see the comment there for why.

        _timerLabel = new Label
        {
            Font = new Font("Segoe UI", 130, FontStyle.Bold),
            ForeColor = AppColors.Success,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "00:00"
        };

        // Sits above the big timer only while a discussion is actually in progress or about to start —
        // toggled alongside ContentHost's visibility in UpdateStatusDisplay so it doesn't also show during
        // the plain idle/Ready states that share this same underlying screen. Plain Dock.Top controls
        // (rather than a custom-painted one) deliberately — a hand-rolled OnPaint here previously ended up
        // not rendering at all in the real fullscreen window despite working in an isolated test, and wasn't
        // worth debugging further when Label/Panel are guaranteed to just work.
        _discussionSpacer = new Panel
        {
            Dock = DockStyle.Top,
            BackColor = AppColors.Background,
            Height = 0,
            Visible = false
        };
        _discussionHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 170,
            Font = new Font("Segoe UI", DiscussionHeaderMaxFontSize, FontStyle.Bold),
            ForeColor = AppColors.DiscussionAction,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = SpacedOut("Muhokama va savol-javoblar"),
            Visible = false
        };
        _discussionAccentLine = new Panel
        {
            Dock = DockStyle.Top,
            Height = 3,
            BackColor = AppColors.DiscussionAction,
            Visible = false
        };
        // Plain blank strip rather than Margin/Padding — Dock layout doesn't respect either between
        // siblings, so a real spacer control is the only reliable way to keep the accent line off the timer.
        _discussionBottomGap = new Panel
        {
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = AppColors.Background,
            Visible = false
        };

        // No top info bar anymore — the operator wanted the projector-facing screen to show as much of the
        // actual slide as possible, with only a small always-on-top close control left floating in the
        // corner (fullscreen mode has no title bar at all, so this is otherwise the only way to close it).
        _closeButton = new Button
        {
            Text = "✕",
            Size = new Size(30, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = AppColors.Danger,
            ForeColor = AppColors.TextPrimary,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.Click += (_, _) => HideAll();

        _miniTimerLabel = new TransparentOverlayLabel
        {
            Font = new Font("Segoe UI", 32, FontStyle.Bold),
            ForeColor = AppColors.Success,
            AutoSize = true,
            Text = "00:00",
            Visible = false
        };

        _controlButton = new RoundedButton
        {
            Text = "MUHOKAMAGA O'TISH",
            BackColor = AppColors.DiscussionAction,
            CornerRadius = 12
        };
        _controlButton.AutoFitToText();
        _controlButton.Click += (_, _) => OnControlButtonClick();

        ContentHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Visible = false };

        // Owned directly rather than DI-injected: PdfSlideDisplayService needs ContentHost itself, and
        // injecting it via the constructor would create a PresentationForm -> service -> PresentationForm
        // cycle. Both .pptx and .pdf end up shown through this same embedded viewer — see
        // PptxToPdfConverter for why .pptx never opens a separate PowerPoint window.
        _pdfDisplayService = new PdfSlideDisplayService(ContentHost);

        _blinkTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkVisible = !_blinkVisible;
            _timerLabel.ForeColor = _blinkVisible ? AppColors.Danger : AppColors.Background;
            // Toggling Visible (rather than a color trick) so the blink reads correctly over arbitrary
            // slide content behind it, not just the plain background color _timerLabel blinks against.
            _miniTimerLabel.Visible = _blinkVisible && ContentHost.Visible;
        };

        Controls.Add(_timerLabel);
        Controls.Add(_discussionBottomGap);
        Controls.Add(_discussionAccentLine);
        Controls.Add(_discussionHeader);
        Controls.Add(_discussionSpacer);
        Controls.Add(ContentHost);
        ContentHost.BringToFront();

        // Added and brought to front last so they stay visible/clickable even while ContentHost is showing
        // live slide content on top of everything else.
        Controls.Add(_closeButton);
        Controls.Add(_miniTimerLabel);
        Controls.Add(_controlButton);
        _closeButton.BringToFront();
        _miniTimerLabel.BringToFront();
        _controlButton.BringToFront();
        Resize += (_, _) => PositionCloseButton();
        PositionCloseButton();
        Resize += (_, _) => PositionOverlayControls();
        PositionOverlayControls();

        Resize += (_, _) => PositionDiscussionHeader();
        Shown += (_, _) => PositionDiscussionHeader();
        PositionDiscussionHeader();

        _session.PresentationChanged += () => RunOnUiThread(UpdatePresentationDisplay);
        _session.StatusChanged += status => RunOnUiThread(() =>
        {
            StopBlink();
            UpdateStatusDisplay(status);
            _ = HandleSlideVisibilityAsync(status);
        });
        _session.TimeTick += (remaining, mode) => RunOnUiThread(() =>
        {
            if (remaining > 0) StopBlink();
            UpdateTimerDisplay(remaining, mode);
            HandleWarningState(remaining);
        });
        _session.TimerExpired += mode => RunOnUiThread(() => _ = OnTimerExpiredAsync(mode));
    }

    /// <summary>Called by AdminForm right before starting a presentation — not shown at app launch, only
    /// once the operator actually presses Boshlash. Positioning is awaited (not fire-and-forget on Load
    /// like before) so <see cref="Bounds"/> is already correct by the time the caller opens the slide.</summary>
    public async Task EnsureVisibleAsync()
    {
        var settings = await _settingsRepository.GetAsync();
        PositionOnTargetMonitor(settings.FullscreenEnabled);
        if (!Visible)
        {
            Show();
        }
    }

    /// <summary>Hides this screen — used both by the X button and by AdminForm once the whole queue is
    /// exhausted, so the operator lands back on the admin window.</summary>
    public void HideAll()
    {
        Hide();
    }

    /// <summary>Gates the actual timer start behind one extra manual click — <see cref="AdminForm"/>'s
    /// Boshlash and the discussion-end picker both call this instead of starting immediately. Order matters
    /// here: the slide is shown on the projector screen FIRST, then the confirmation gate appears, so the
    /// operator (and whoever's watching the gate) can actually see the slide is loaded and ready before
    /// deciding to start the countdown — asking "start the timer?" before anything is even on screen would
    /// defeat the point. The session must already have the target presentation selected (Status
    /// Ready/Waiting) before this is called.</summary>
    public async Task StartSelectedPresentationAsync()
    {
        var current = _session.CurrentPresentation;
        if (current is null)
        {
            return;
        }

        await EnsureVisibleAsync();
        await ShowSlidePreviewAsync();

        using var gate = new StartPresentationGateForm(current.FullName, current.Title);
        if (gate.ShowDialog() != DialogResult.OK)
        {
            // Slide stays up, timer stays off — Boshlash can simply be pressed again later to re-open this
            // same gate rather than leaving the operator stuck on a half-started screen.
            return;
        }

        await _session.StartPresentationAsync();

        // The gate dialog just closed, taking OS keyboard focus with it — without reclaiming it here, the
        // slide's own NavigationCompleted/ShowAsync Focus() calls land on a projector window that isn't
        // actually the foreground/activated one at the OS level, so WinForms' Focus() is a no-op and arrow
        // keys/PageUp/PageDown silently do nothing until the operator clicks the slide with the mouse.
        // Activate() first so the subsequent Focus() actually sticks.
        Activate();
        await _pdfDisplayService.ShowAsync();
    }

    /// <summary>Opens (or re-shows) the current presentation's slide on the projector screen without
    /// touching <see cref="PresentationSessionController.Status"/> or the timer — this is the "preview"
    /// state the confirmation gate sits on top of. Mirrors the Running branch of
    /// <see cref="HandleSlideVisibilityAsync"/>, since <see cref="StartPresentationAsync"/>'s own later
    /// transition to Running will find the slide already open (same presentation Id) and just re-show it
    /// instead of loading it a second time.</summary>
    private async Task ShowSlidePreviewAsync()
    {
        try
        {
            ContentHost.Visible = true;
            if (_slideOpen && _openPresentationId == _session.CurrentPresentation?.Id)
            {
                await _pdfDisplayService.ShowAsync();
            }
            else
            {
                await OpenCurrentSlideAsync();
            }

            _miniTimerLabel.BringToFront();
            _controlButton.BringToFront();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Slaydni ko'rsatishda xatolik: {ex.Message}", "Namoyish Ekrani", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PositionOnTargetMonitor(bool fullscreenSetting)
    {
        var hasSecondMonitor = Screen.AllScreens.Length > 1;
        var target = hasSecondMonitor ? Screen.AllScreens.First(s => !s.Primary) : Screen.PrimaryScreen!;

        // TopMost + hidden-from-taskbar only makes sense when this screen owns a dedicated monitor. On a
        // single-monitor machine that combination would permanently bury AdminForm behind it with no way
        // back — no taskbar entry, and Alt+Tab can't out-rank TopMost — which is exactly the "app is stuck
        // showing just the timer" trap. So on a single monitor this stays a normal, switchable window even
        // while still covering the whole screen.
        TopMost = hasSecondMonitor;
        ShowInTaskbar = !hasSecondMonitor;

        if (fullscreenSetting)
        {
            FormBorderStyle = FormBorderStyle.None;
            Bounds = target.Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = new Rectangle(target.Bounds.X + 60, target.Bounds.Y + 60, 1000, 700);
        }
    }

    /// <summary>The control bar's single button does one of three different things depending on
    /// <see cref="PresentationSessionController.Status"/> — see <see cref="UpdateControlButton"/> for the
    /// matching label on each.</summary>
    private async void OnControlButtonClick()
    {
        try
        {
            if (_session.Status == PresentationStatus.DiscussionReady)
            {
                // Starting the (already-waiting) discussion clock isn't "early" for anything — there's
                // nothing running yet to interrupt — so it never needs the confirmation dialog below.
                await _session.StartDiscussionAsync();
                return;
            }

            var inDiscussion = _session.ActiveTimerMode == TimerMode.Discussion;
            var remaining = inDiscussion ? _session.DiscussionRemainingSeconds : _session.PresentationRemainingSeconds;

            if (remaining > 0)
            {
                // Next never skips discussion — from the presentation phase it only jumps ahead to
                // Discussion (mirrors what happens automatically once the timer hits zero); only from
                // Discussion does it actually finish and move to the next presenter. The confirmation text
                // must say which one is about to happen, not always "move to next presenter".
                var question = inDiscussion
                    ? "Muhokama vaqti hali tugamagan. Keyingi taqdimotchiga o'tishni xohlaysizmi?"
                    : "Taqdimot vaqti hali tugamagan. Muhokamaga o'tishni xohlaysizmi?";
                var confirm = MessageBox.Show(this, question,
                    "Tasdiqlash", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            if (inDiscussion)
            {
                // Deliberately doesn't finish/touch the current presentation until the picker below actually
                // returns a choice — so the discussion screen underneath (header, big timer) stays exactly
                // as it was for as long as the picker sits open on top of it, and a cancelled picker leaves
                // discussion running untouched instead of stranding the operator on a blank screen.
                await ShowNextPresentationPickerAsync();
            }
            else
            {
                await _session.NextPresenterAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Amal bajarilmadi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Shown modally over this screen once a discussion is finished — lets the operator choose
    /// which queued presentation starts next instead of the queue silently advancing on its own. The actual
    /// finish/select/start sequence deliberately runs here, after <c>ShowDialog</c> returns, rather than
    /// from inside the picker's own button click — running those (real async DB + WebView2) calls from a
    /// handler nested inside the dialog's own modal message loop made the presentation silently fail to
    /// start once picked.</summary>
    private async Task ShowNextPresentationPickerAsync()
    {
        using var picker = new PresentationPickerForm(_session);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedPresentationId is not { } presentationId)
        {
            return;
        }

        await _session.FinishCurrentPresentationAsync();
        await _session.SelectPresentationAsync(presentationId);
        await StartSelectedPresentationAsync();
    }

    /// <summary>Alarm + AutoNext live here rather than in AdminForm now, since this screen is the one the
    /// operator is actually watching while a timer runs.</summary>
    private async Task OnTimerExpiredAsync(TimerMode mode)
    {
        StartBlink();
        await PlayFinalAlarm();

        var settings = await _settingsRepository.GetAsync();

        // Presentation-timer expiry auto-starts discussion on its own (PresentationSessionController) —
        // AutoNext here only ever needs to cover advancing past a finished discussion.
        if (settings.AutoNext && mode == TimerMode.Discussion)
        {
            await _session.NextPresenterAsync();
        }
    }

    /// <summary>Drives the last <see cref="WarningThresholdSeconds"/> seconds of either timer: a short beep
    /// plus a gold/red blink, both derived from the same <paramref name="remainingSeconds"/> value so they
    /// can never drift out of sync with each other — no separate blink timer or mutable toggle state needed.
    /// Kept deliberately separate from <see cref="UpdateTimerDisplay"/> (which runs first and sets the
    /// milder 60s/30s color tiers) so this one urgent-countdown concern doesn't get tangled up with the
    /// routine "what does the clock say right now" concern.</summary>
    private void HandleWarningState(int remainingSeconds)
    {
        if (remainingSeconds is <= 0 or > WarningThresholdSeconds)
        {
            return;
        }

        var blinkColor = remainingSeconds % 2 == 0 ? AppColors.TimerCriticalRed : AppColors.TimerWarningGold;
        _timerLabel.ForeColor = blinkColor;
        _miniTimerLabel.ForeColor = blinkColor;

        _ = PlayWarningSound();
    }

    /// <summary>Short once-a-second beep for <see cref="HandleWarningState"/>'s 10-through-1 countdown —
    /// deliberately the quieter tick sound (not the louder final alarm) since this fires ten times in a row.
    /// Still governed by AlarmEnabled so operators who disabled sound entirely get none of this either.</summary>
    private async Task PlayWarningSound()
    {
        var settings = await _settingsRepository.GetAsync();
        if (settings.AlarmEnabled)
        {
            _alarmSoundService.PlayTick();
        }
    }

    /// <summary>Longer, more insistent alarm for the moment the countdown actually reaches 00:00 — fired
    /// once from <see cref="OnTimerExpiredAsync"/>, distinct from the per-second <see cref="PlayWarningSound"/>
    /// beeps leading up to it.</summary>
    private async Task PlayFinalAlarm()
    {
        var settings = await _settingsRepository.GetAsync();
        if (settings.AlarmEnabled)
        {
            _alarmSoundService.Play(settings.AlarmSoundPath);
        }
    }

    private void PositionCloseButton()
    {
        _closeButton.Location = new Point(ClientSize.Width - _closeButton.Width - 8, 4);
    }

    /// <summary>Bottom-corner placement for <see cref="_miniTimerLabel"/> and <see cref="_controlButton"/> —
    /// each sized to its own content with no backing bar behind them, mirroring how <see cref="_closeButton"/>
    /// floats over <see cref="ContentHost"/>.</summary>
    private void PositionOverlayControls()
    {
        const int margin = 24;
        _miniTimerLabel.Location = new Point(margin, ClientSize.Height - _miniTimerLabel.Height - margin);
        _controlButton.Location = new Point(
            ClientSize.Width - _controlButton.Width - margin,
            ClientSize.Height - _controlButton.Height - margin);
    }

    /// <summary>Grows <see cref="_discussionSpacer"/> (a plain Dock.Top blank strip sitting above the
    /// header) just enough to give the header some breathing room below the top edge, without eating so
    /// much height that the big timer's own Dock.Fill area — which needs to dominate the screen and read as
    /// centered — gets squeezed down into a small strip at the bottom. Deliberately a small fraction of
    /// ClientSize rather than a calculation tied to the timer's own font metrics: this only needs to land
    /// "off the top edge", and a fixed fraction is far less likely to be thrown off by a stale ClientSize
    /// read during a status transition.</summary>
    private void PositionDiscussionHeader()
    {
        _discussionSpacer.Height = Math.Max(0, ClientSize.Height / 12);
        FitDiscussionHeaderFont();
    }

    /// <summary>Shrinks <see cref="_discussionHeader"/>'s font, starting from <see cref="DiscussionHeaderMaxFontSize"/>,
    /// until the (letter-spaced) headline actually fits within the window's width — otherwise trailing
    /// characters silently draw past the label's right edge and get clipped by it. Re-run alongside the
    /// spacer on every resize since "fits" depends on the current window width.</summary>
    private void FitDiscussionHeaderFont()
    {
        var maxWidth = Math.Max(1, ClientSize.Width - 40);
        var size = DiscussionHeaderMaxFontSize;
        while (size > DiscussionHeaderMinFontSize && !FitsWidth(size))
        {
            size -= 2;
        }

        bool FitsWidth(int fontSize)
        {
            using var trialFont = new Font("Segoe UI", fontSize, FontStyle.Bold);
            return TextRenderer.MeasureText(_discussionHeader.Text, trialFont).Width <= maxWidth;
        }

        if (_discussionHeader.Font.Size != size)
        {
            var oldFont = _discussionHeader.Font;
            _discussionHeader.Font = new Font("Segoe UI", size, FontStyle.Bold);
            oldFont.Dispose();
        }
    }

    /// <summary>Fakes letter-spacing for the discussion header — a plain Label can't do real character
    /// tracking, but interposing thin spaces reads close enough for a big bold conference-room headline.</summary>
    private static string SpacedOut(string text) => string.Join(" ", text.ToUpperInvariant().Select(c => c.ToString()));

    private void RunOnUiThread(Action action)
    {
        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void UpdatePresentationDisplay()
    {
        UpdateControlButton();

        // Loading/advancing a presentation resets the timer values on the controller, but that alone
        // doesn't raise TimeTick (nothing is ticking yet) — without this the countdown stayed frozen
        // at its initial "00:00" text until the operator actually pressed Start.
        UpdateTimerDisplay(
            _session.ActiveTimerMode == TimerMode.Discussion ? _session.DiscussionRemainingSeconds : _session.PresentationRemainingSeconds,
            _session.ActiveTimerMode);
    }

    private void UpdateStatusDisplay(PresentationStatus status)
    {
        UpdateControlButton();

        // Real slide content only covers the screen while the presentation timer itself is active;
        // every other state (waiting/discussion/finished) shows our own info screen underneath.
        ContentHost.Visible = status is PresentationStatus.Running;

        // The mini timer only needs to exist while the slide is actually hiding the big centered one behind
        // it — everywhere else (including Discussion) that centered timer is already visible on its own, so
        // showing this too would just duplicate it.
        _miniTimerLabel.Visible = ContentHost.Visible;

        var discussionActive = status is PresentationStatus.Discussion or PresentationStatus.DiscussionReady or PresentationStatus.DiscussionPaused;
        _discussionSpacer.Visible = discussionActive;
        _discussionHeader.Visible = discussionActive;
        _discussionAccentLine.Visible = discussionActive;
        _discussionBottomGap.Visible = discussionActive;
        PositionDiscussionHeader();

        try
        {
            System.IO.File.AppendAllText(
                @"C:\Users\a.soxibov\AppData\Local\Temp\claude\E--Anvar-Projects-Inno-viwer\747d5aab-f6b0-44e4-b5aa-6dbd3b605590\scratchpad\statuslog.txt",
                $"{DateTime.Now:HH:mm:ss.fff} status={status} discussionActive={discussionActive} ClientSize={ClientSize} spacer.H={_discussionSpacer.Height} spacer.V={_discussionSpacer.Visible} header.Top={_discussionHeader.Top} header.V={_discussionHeader.Visible}\n");
        }
        catch { /* diagnostic only */ }
    }

    private void UpdateControlButton()
    {
        _controlButton.Enabled = _session.CurrentPresentation is not null;
        _controlButton.Text = _session.Status switch
        {
            PresentationStatus.Running or PresentationStatus.Paused => "MUHOKAMAGA O'TISH",
            PresentationStatus.DiscussionReady => "BOSHLASH",
            _ => "KEYINGI"
        };

        // Text length varies by state ("BOSHLASH" vs "MUHOKAMAGA O'TISH"), so both the fitted size and the
        // bottom-right-anchored position need recomputing every time, not just once at construction.
        _controlButton.AutoFitToText();
        PositionOverlayControls();
    }

    /// <summary>
    /// Drives the real slide content in lockstep with the state machine: Running shows it (opening fresh
    /// the first time), Discussion hides it without closing (kept ready in case we resume showing it),
    /// every other state (Finished/Skipped/next presenter) closes it.
    /// </summary>
    private async Task HandleSlideVisibilityAsync(PresentationStatus status)
    {
        try
        {
            switch (status)
            {
                case PresentationStatus.Running:
                    if (_slideOpen && _openPresentationId == _session.CurrentPresentation?.Id)
                    {
                        await _pdfDisplayService.ShowAsync();
                    }
                    else
                    {
                        await OpenCurrentSlideAsync();
                    }
                    break;

                case PresentationStatus.Discussion or PresentationStatus.DiscussionReady:
                    if (_slideOpen)
                    {
                        await _pdfDisplayService.HideAsync();
                    }
                    break;

                default:
                    // Finished, Skipped, Ready (next/previous presenter), Waiting.
                    await CloseActiveSlideAsync();
                    break;
            }

            _miniTimerLabel.BringToFront();
            _controlButton.BringToFront();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Slaydni ko'rsatishda xatolik: {ex.Message}", "Namoyish Ekrani", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OpenCurrentSlideAsync()
    {
        var current = _session.CurrentPresentation;
        if (current is null)
        {
            return;
        }

        var absolutePath = _fileStorageService.GetAbsolutePath(current.FilePath);
        if (current.FileType == PresentationFileType.Pptx)
        {
            // Converted (once, cached) to PDF and shown through the same embedded viewer as real PDFs —
            // no separate PowerPoint window ever opens. See PptxToPdfConverter's doc comment for the trade-off.
            absolutePath = await PptxToPdfConverter.EnsureConvertedToPdfAsync(absolutePath);
        }

        var bounds = new ScreenBounds(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height);
        await _pdfDisplayService.OpenAsync(absolutePath, bounds);
        _slideOpen = true;
        _openPresentationId = current.Id;
    }

    private async Task CloseActiveSlideAsync()
    {
        if (!_slideOpen)
        {
            return;
        }

        await _pdfDisplayService.CloseAsync();
        _slideOpen = false;
        _openPresentationId = null;
    }

    private void StartBlink()
    {
        _blinkVisible = true;
        _blinkTimer.Start();
    }

    private void StopBlink()
    {
        _blinkTimer.Stop();
        _blinkVisible = true;
    }

    private void UpdateTimerDisplay(int remainingSeconds, TimerMode mode)
    {
        var span = TimeSpan.FromSeconds(remainingSeconds);
        var text = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
        var color = remainingSeconds <= 30 ? AppColors.Danger
            : remainingSeconds <= 60 ? AppColors.Warning
            : AppColors.Success;

        _timerLabel.Text = text;
        _timerLabel.ForeColor = color;
        _miniTimerLabel.Text = text;
        _miniTimerLabel.ForeColor = color;
        PositionOverlayControls();
    }
}
