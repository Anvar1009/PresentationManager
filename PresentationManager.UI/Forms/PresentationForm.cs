using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Enums;
using PresentationManager.UI.Controls;
using PresentationManager.UI.SlideDisplay;
using PresentationManager.UI.Theme;

namespace PresentationManager.UI.Forms;

/// <summary>
/// The projector/shared-screen-facing display — pure output, no controls at all (not even a close button):
/// only the live timer and the slide itself ever appear here, since this window is the one actually mirrored
/// to the audience (e.g. via a screen share of just this window). Every operator action — starting,
/// advancing to discussion, choosing what's next, hiding this screen — happens from <see cref="AdminForm"/>
/// instead. Fullscreen on the second monitor when one exists, otherwise fullscreen on the primary monitor.
/// </summary>
public sealed class PresentationForm : Form
{
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

    /// <summary>Small always-visible countdown shown over the slide while it's covering the big centered
    /// <see cref="_timerLabel"/> — a plain floating label, not a backing panel/bar, so nothing but the
    /// digits themselves is ever on screen.</summary>
    private readonly TransparentOverlayLabel _miniTimerLabel;

    /// <summary>Static "MUHOKAMA VAQTI" caption floating just above <see cref="_miniTimerLabel"/> — shown
    /// only while that countdown is actually counting discussion time (<see cref="PresentationStatus.DiscussionReady"/>
    /// or <see cref="PresentationStatus.Discussion"/>), so the operator can tell at a glance which clock the
    /// digits underneath belong to instead of confusing it for the presentation countdown.</summary>
    private readonly TransparentOverlayLabel _miniTimerCaptionLabel;

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

        _miniTimerLabel = new TransparentOverlayLabel
        {
            Font = new Font("Segoe UI", 32, FontStyle.Bold),
            ForeColor = AppColors.Success,
            AutoSize = true,
            Text = "00:00",
            Visible = false
        };

        _miniTimerCaptionLabel = new TransparentOverlayLabel
        {
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = AppColors.DiscussionAction,
            // Floats over arbitrary slide content, unlike every other themed control here that only ever
            // sits on the fixed dark app background — a solid dark pill (rather than just an outline) keeps
            // the gold text legible and reads as a deliberate tag/badge no matter what's behind it.
            PillColor = AppColors.Panel,
            PillBorderColor = AppColors.DiscussionAction,
            Padding = new Padding(18, 7, 18, 7),
            AutoSize = true,
            Text = "MUHOKAMA VAQTI",
            Visible = false
        };

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
        Controls.Add(ContentHost);
        ContentHost.BringToFront();

        // Added and brought to front last so they stay visible even while ContentHost is showing live slide
        // content on top of everything else.
        Controls.Add(_miniTimerLabel);
        Controls.Add(_miniTimerCaptionLabel);
        _miniTimerLabel.BringToFront();
        _miniTimerCaptionLabel.BringToFront();
        Resize += (_, _) => PositionOverlayControls();
        PositionOverlayControls();

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

    /// <summary>Hides this screen — called from AdminForm, either manually (a menu action) or automatically
    /// once the whole queue is exhausted, so the operator lands back on the admin window.</summary>
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
        if (!await ShowSlidePreviewAsync())
        {
            // Slide failed to open (error already shown to the operator inside ShowSlidePreviewAsync) —
            // bail out here instead of proceeding to the gate/timer over a black, empty screen. Boshlash
            // can simply be pressed again once whatever's wrong (missing file, PowerPoint unavailable, ...)
            // is fixed.
            return;
        }

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
    /// <returns>false if the slide failed to open — the caller must not proceed to starting the timer in
    /// that case, since there would be nothing but the black <see cref="ContentHost"/> panel to show for it.</returns>
    private async Task<bool> ShowSlidePreviewAsync()
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
            return true;
        }
        catch (Exception ex)
        {
            // Nothing actually loaded — hide the bare black panel rather than leaving it up over nothing.
            ContentHost.Visible = false;
            MessageBox.Show(this, $"Slaydni ko'rsatishda xatolik: {ex.Message}", "Namoyish Ekrani", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
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

    /// <summary>Alarm + AutoNext live here rather than in AdminForm now, since this screen is the one the
    /// operator is actually watching while a timer runs.</summary>
    private async Task OnTimerExpiredAsync(TimerMode mode)
    {
        StartBlink();
        await PlayFinalAlarm();

        var settings = await _settingsRepository.GetAsync();

        // Presentation-timer expiry auto-starts discussion on its own (PresentationSessionController) —
        // AutoNext here only ever needs to cover advancing past a finished discussion. When the presentation
        // has extra discussion time configured, discussion expiry instead auto-parks on ExtraDiscussionReady
        // (same controller) and waits for the operator's confirmation there, regardless of AutoNext — so
        // this must not race ahead and finish the presentation out from under that prompt. Once the extra
        // clock itself runs out, there's no further phase to wait for, so AutoNext applies normally again.
        var hasExtraDiscussion = (_session.CurrentPresentation?.ExtraDiscussionTimeSeconds ?? 0) > 0;
        var shouldAutoAdvance = settings.AutoNext
            && (mode == TimerMode.ExtraDiscussion || (mode == TimerMode.Discussion && !hasExtraDiscussion));
        if (shouldAutoAdvance)
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

    /// <summary>Bottom-left placement for <see cref="_miniTimerLabel"/> — sized to its own content with no
    /// backing bar behind it, floating directly over <see cref="ContentHost"/>. <see cref="_miniTimerCaptionLabel"/>
    /// is centered directly above the mini timer's digits (rather than sharing their left edge) so the badge
    /// reads as attached to the clock beneath it, whether or not it's actually visible right now.</summary>
    private void PositionOverlayControls()
    {
        const int margin = 24;
        const int captionGap = 10;
        _miniTimerLabel.Location = new Point(margin, ClientSize.Height - _miniTimerLabel.Height - margin);
        _miniTimerCaptionLabel.Location = new Point(
            _miniTimerLabel.Left + (_miniTimerLabel.Width - _miniTimerCaptionLabel.Width) / 2,
            _miniTimerLabel.Top - _miniTimerCaptionLabel.Height - captionGap);
    }

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
        // Loading/advancing a presentation resets the timer values on the controller, but that alone
        // doesn't raise TimeTick (nothing is ticking yet) — without this the countdown stayed frozen
        // at its initial "00:00" text until the operator actually pressed Start.
        var remaining = _session.ActiveTimerMode switch
        {
            TimerMode.Discussion => _session.DiscussionRemainingSeconds,
            TimerMode.ExtraDiscussion => _session.ExtraDiscussionRemainingSeconds,
            _ => _session.PresentationRemainingSeconds
        };
        UpdateTimerDisplay(remaining, _session.ActiveTimerMode);
    }

    private void UpdateStatusDisplay(PresentationStatus status)
    {
        // The slide covers the screen for both the presentation itself and its discussion phase (including
        // the optional extra discussion phase) — there is no separate discussion screen to switch to
        // anymore, so it simply stays up and the big centered timer underneath just switches to counting
        // down whichever clock is active instead.
        ContentHost.Visible = status is PresentationStatus.Running or PresentationStatus.Discussion or PresentationStatus.DiscussionReady
            or PresentationStatus.ExtraDiscussionReady or PresentationStatus.ExtraDiscussion;

        // The mini timer only needs to exist while the slide is actually hiding the big centered one behind
        // it — everywhere else that centered timer is already visible on its own, so showing this too would
        // just duplicate it.
        _miniTimerLabel.Visible = ContentHost.Visible;

        // Labels which clock the mini timer's digits actually belong to right now — DiscussionReady/
        // ExtraDiscussionReady are reached before their respective Start*DiscussionAsync has run (so
        // ActiveTimerMode is still whatever it was before), hence checking Status here rather than the
        // timer's own mode.
        var discussionActive = status is PresentationStatus.Discussion or PresentationStatus.DiscussionReady
            or PresentationStatus.ExtraDiscussionReady or PresentationStatus.ExtraDiscussion;
        _miniTimerCaptionLabel.Visible = discussionActive && ContentHost.Visible;
        _miniTimerCaptionLabel.Text = status is PresentationStatus.ExtraDiscussionReady or PresentationStatus.ExtraDiscussion
            ? "QO'SHIMCHA MUHOKAMA VAQTI"
            : "MUHOKAMA VAQTI";

        if (status == PresentationStatus.DiscussionReady)
        {
            // Parked, not yet ticking — show the discussion duration it's about to start from instead of
            // leaving the mini timer frozen on whatever the presentation countdown last displayed.
            UpdateTimerDisplay(_session.DiscussionRemainingSeconds, TimerMode.Discussion);
        }
        else if (status == PresentationStatus.ExtraDiscussionReady)
        {
            UpdateTimerDisplay(_session.ExtraDiscussionRemainingSeconds, TimerMode.ExtraDiscussion);
        }
    }

    /// <summary>
    /// Drives the real slide content in lockstep with the state machine: Running and the discussion phase
    /// both show it (opening fresh the first time) — there is no separate discussion screen to switch to
    /// anymore, so the slide simply stays up underneath the discussion countdown. Every other state
    /// (Finished/Skipped/next presenter) closes it.
    /// </summary>
    private async Task HandleSlideVisibilityAsync(PresentationStatus status)
    {
        try
        {
            switch (status)
            {
                case PresentationStatus.Running or PresentationStatus.Discussion or PresentationStatus.DiscussionReady
                    or PresentationStatus.ExtraDiscussionReady or PresentationStatus.ExtraDiscussion:
                    if (_slideOpen && _openPresentationId == _session.CurrentPresentation?.Id)
                    {
                        await _pdfDisplayService.ShowAsync();
                    }
                    else
                    {
                        await OpenCurrentSlideAsync();
                    }
                    break;

                default:
                    // Finished, Skipped, Ready (next/previous presenter), Waiting.
                    await CloseActiveSlideAsync();
                    break;
            }

            _miniTimerLabel.BringToFront();
        }
        catch (Exception ex)
        {
            // Unlike ShowSlidePreviewAsync, the timer/status transition here has already happened by the
            // time this runs (this path only fires from auto-advance, not the Boshlash gate) so it can't be
            // aborted — hiding the bare black panel is the best that can be done, on top of telling the
            // operator what went wrong.
            ContentHost.Visible = false;
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

        var absolutePath = await _fileStorageService.GetAbsolutePathAsync(current.FilePath);
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
