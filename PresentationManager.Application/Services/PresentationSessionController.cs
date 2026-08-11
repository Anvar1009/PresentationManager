using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.Application.Services;

/// <summary>
/// The presentation/discussion state machine. Flow is linear and mostly automatic: starting a presentation
/// counts down to zero, at which point discussion starts automatically (no manual "start discussion" step);
/// the operator's only actions are Start and Next.
/// </summary>
public sealed class PresentationSessionController
{
    private readonly IPresentationRepository _presentationRepository;
    private readonly IHistoryRepository _historyRepository;
    private readonly TimerEngine _timer;

    private List<Presentation> _queue = [];
    private int _currentIndex = -1;

    public PresentationSessionController(
        IPresentationRepository presentationRepository,
        IHistoryRepository historyRepository,
        TimerEngine timer)
    {
        _presentationRepository = presentationRepository;
        _historyRepository = historyRepository;
        _timer = timer;
        _timer.Tick += (remaining) =>
        {
            if (_timer.Mode == TimerMode.Presentation) PresentationRemainingSeconds = remaining;
            else if (_timer.Mode == TimerMode.Discussion) DiscussionRemainingSeconds = remaining;
            else if (_timer.Mode == TimerMode.ExtraDiscussion) ExtraDiscussionRemainingSeconds = remaining;
            TimeTick?.Invoke(remaining, _timer.Mode);
        };
        _timer.Expired += () => OnTimerExpired(_timer.Mode);
    }

    public Presentation? CurrentPresentation => _currentIndex >= 0 && _currentIndex < _queue.Count ? _queue[_currentIndex] : null;

    public Presentation? NextPresentation =>
        _currentIndex >= 0 && _currentIndex + 1 < _queue.Count ? _queue[_currentIndex + 1] : null;

    public IReadOnlyList<Presentation> Queue => _queue;

    /// <summary>The project whose presentations <see cref="Queue"/> currently reflects — null means no
    /// project has been selected yet (e.g. first launch before any project exists).</summary>
    public int? CurrentProjectId { get; private set; }

    public PresentationStatus Status { get; private set; } = PresentationStatus.Waiting;

    public int PresentationRemainingSeconds { get; private set; }

    public int DiscussionRemainingSeconds { get; private set; }

    public int ExtraDiscussionRemainingSeconds { get; private set; }

    public TimerMode ActiveTimerMode => _timer.Mode;

    /// <summary>Raised whenever <see cref="Status"/> changes.</summary>
    public event Action<PresentationStatus>? StatusChanged;

    /// <summary>Raised every second while a timer is running: (remainingSeconds, mode).</summary>
    public event Action<int, TimerMode>? TimeTick;

    /// <summary>Raised when the active timer (presentation or discussion) hits zero.</summary>
    public event Action<TimerMode>? TimerExpired;

    /// <summary>Raised whenever the current/next presentation changes (queue navigation).</summary>
    public event Action? PresentationChanged;

    /// <summary>First load on app startup — selects <paramref name="projectId"/> (typically the operator's
    /// last-active project) as the current project and loads its queue. Pass null when no project is
    /// selectable yet (e.g. very first launch, before any project has been created).</summary>
    public async Task InitializeAsync(int? projectId, CancellationToken ct = default)
    {
        CurrentProjectId = projectId;
        _queue = projectId is int id ? await _presentationRepository.GetAllOrderedAsync(id, ct) : [];
        // -1 (not found) means every presentation in the queue is already Finished/Skipped; leaving it at
        // -1 here reports "no current presentation" instead of resurrecting the last finished one as Ready.
        _currentIndex = _queue.FindIndex(p => p.Status != PresentationStatus.Finished && p.Status != PresentationStatus.Skipped);

        ResetTimersForCurrent();
        Status = CurrentPresentation is null ? PresentationStatus.Waiting : PresentationStatus.Ready;

        // Both events fire here (not just PresentationChanged) so every subscriber — including the
        // Presentation Screen's mode label and timer readout — is fully synced on first load, not just
        // whatever happens to be wired to PresentationChanged.
        StatusChanged?.Invoke(Status);
        PresentationChanged?.Invoke();
    }

    /// <summary>Switches the active project (e.g. the operator picked a different one from the Loyihalar
    /// dialog) and reloads the queue to that project's presentations. Any timer in progress is stopped —
    /// switching projects is an explicit operator action, so nothing should keep running against the
    /// project being navigated away from.</summary>
    public async Task SetActiveProjectAsync(int? projectId, CancellationToken ct = default)
    {
        _timer.Stop();
        await InitializeAsync(projectId, ct);
    }

    /// <summary>Reloads the queue from the database after an external CRUD/reorder operation, preserving
    /// the current pointer by presentation Id where possible. No-ops if no project is currently active.
    /// Also called on a timer by AdminForm so presentations submitted via the Telegram bot show up on their
    /// own — which is why this must never disturb a presentation that's actively Running/Paused/in
    /// discussion: it used to unconditionally reset the current presentation's timers and force
    /// <see cref="Status"/> back to Ready on every call, which was harmless when this only ever ran after an
    /// explicit CRUD action taken while the queue was idle, but turned into a real bug once a periodic
    /// caller could invoke it mid-presentation — the slide would silently drop out from under the operator
    /// every time it fired, with the countdown reset to full and stranded on Ready.</summary>
    public async Task ReloadQueueAsync(CancellationToken ct = default)
    {
        if (CurrentProjectId is not int projectId)
        {
            return;
        }

        var currentId = CurrentPresentation?.Id;
        var wasActive = CurrentPresentation is not null && Status is not (PresentationStatus.Waiting or PresentationStatus.Ready);

        _queue = await _presentationRepository.GetAllOrderedAsync(projectId, ct);
        _currentIndex = currentId is null ? -1 : _queue.FindIndex(p => p.Id == currentId);

        if (wasActive && _currentIndex >= 0)
        {
            // Still mid-flow on the same presentation - only the list contents changed (e.g. a new
            // bot-submitted presentation appeared further down the queue). Leave Status/timers alone and
            // just let subscribers know the queue itself is different.
            PresentationChanged?.Invoke();
            return;
        }

        if (_currentIndex < 0)
        {
            // No current pointer — either the queue was empty/exhausted before this reload, or whatever
            // was current got deleted. Pick the first not-yet-finished/skipped entry (same rule
            // InitializeAsync uses) so a presentation just added after the queue finished becomes current,
            // instead of naively landing on index 0 and resurrecting an already-Finished one.
            _currentIndex = _queue.FindIndex(p => p.Status != PresentationStatus.Finished && p.Status != PresentationStatus.Skipped);
        }

        if (_currentIndex >= 0)
        {
            ResetTimersForCurrent();
            Status = PresentationStatus.Ready;
        }
        else
        {
            Status = PresentationStatus.Waiting;
        }

        StatusChanged?.Invoke(Status);
        PresentationChanged?.Invoke();
    }

    /// <summary>Explicitly (re-)selects a presentation from the queue as the current one, resetting it to
    /// Ready regardless of its previous status — this is how the operator re-presents something that was
    /// already marked Finished, e.g. re-running the same file for a demo/test.</summary>
    public async Task SelectPresentationAsync(int presentationId, CancellationToken ct = default)
    {
        var index = _queue.FindIndex(p => p.Id == presentationId);
        if (index < 0)
        {
            return;
        }

        _timer.Stop();
        _currentIndex = index;
        ResetTimersForCurrent();
        Status = PresentationStatus.Ready;

        await PersistStatusAsync(PresentationStatus.Ready, ct);
        await LogAsync(HistoryEventType.NextPresenter, $"Qayta tanlandi: {CurrentPresentation!.Title}", ct);

        StatusChanged?.Invoke(Status);
        PresentationChanged?.Invoke();
    }

    public async Task StartPresentationAsync(CancellationToken ct = default)
    {
        if (CurrentPresentation is null || Status is not (PresentationStatus.Ready or PresentationStatus.Waiting))
        {
            return;
        }

        _timer.Start(PresentationRemainingSeconds, TimerMode.Presentation);
        await SetStatusAsync(PresentationStatus.Running, HistoryEventType.Started,
            $"Taqdimot boshlandi: {CurrentPresentation.Title}", ct);
    }

    /// <summary>
    /// Advances the presentation to whatever phase comes next — it never skips discussion. From
    /// Running/Paused (presentation not over yet) this moves to <see cref="PresentationStatus.DiscussionReady"/>
    /// — the discussion phase, but the discussion clock stays parked at its full duration until the operator
    /// explicitly calls <see cref="StartDiscussionAsync"/>; only from Discussion/DiscussionPaused does Next
    /// actually finish the current presentation and advance the queue, immediately starting whatever it
    /// lands on next (see <see cref="AdvanceAsync"/>). A presentation that's merely Ready (never started) is
    /// left untouched — on the fullscreen Namoyish Ekrani, Keyingi is the only button visible once a
    /// presentation ends, so a stray/early click here used to finish the next presentation without ever
    /// showing it, silently consuming the whole queue. If the caller needs to confirm an early "time hasn't
    /// run out yet" click with the operator first, that check belongs at the UI layer.
    /// </summary>
    public async Task NextPresenterAsync(CancellationToken ct = default)
    {
        if (CurrentPresentation is null)
        {
            return;
        }

        switch (Status)
        {
            case PresentationStatus.Running or PresentationStatus.Paused:
                _timer.Stop();
                await SetStatusAsync(PresentationStatus.DiscussionReady, HistoryEventType.NextPresenter,
                    "Muhokamaga o'tildi (boshlanishi kutilmoqda)", ct);
                break;

            case PresentationStatus.Discussion or PresentationStatus.DiscussionPaused
                or PresentationStatus.ExtraDiscussionReady or PresentationStatus.ExtraDiscussion:
                _timer.Stop();
                await PersistStatusAsync(PresentationStatus.Finished, ct);
                await LogAsync(HistoryEventType.Finished, $"Taqdimot yakunlandi: {CurrentPresentation.Title}", ct);
                await AdvanceAsync(ct);
                break;
        }
    }

    /// <summary>Ends the current presentation's discussion phase and marks it Finished, but — unlike
    /// <see cref="NextPresenterAsync"/>'s Discussion branch — does not auto-advance to (and start) whatever
    /// comes next in queue order. Used by the manual "Keyingi" click during discussion, which instead pops
    /// open a picker so the operator explicitly chooses the next presentation themselves.</summary>
    public async Task FinishCurrentPresentationAsync(CancellationToken ct = default)
    {
        if (CurrentPresentation is null || Status is not (PresentationStatus.Discussion or PresentationStatus.DiscussionPaused
            or PresentationStatus.ExtraDiscussionReady or PresentationStatus.ExtraDiscussion))
        {
            return;
        }

        _timer.Stop();
        await PersistStatusAsync(PresentationStatus.Finished, ct);
        await LogAsync(HistoryEventType.Finished, $"Taqdimot yakunlandi: {CurrentPresentation.Title}", ct);

        _currentIndex = -1;
        ResetTimersForCurrent();
        Status = PresentationStatus.Waiting;
        StatusChanged?.Invoke(Status);
        PresentationChanged?.Invoke();
    }

    /// <summary>Starts the discussion clock once the operator is actually ready for it — the counterpart to
    /// <see cref="StartPresentationAsync"/>, only reachable from <see cref="PresentationStatus.DiscussionReady"/>.</summary>
    public async Task StartDiscussionAsync(CancellationToken ct = default)
    {
        if (CurrentPresentation is null || Status != PresentationStatus.DiscussionReady)
        {
            return;
        }

        _timer.Start(DiscussionRemainingSeconds, TimerMode.Discussion);
        await SetStatusAsync(PresentationStatus.Discussion, HistoryEventType.DiscussionStarted,
            "Muhokama boshlandi", ct);
    }

    /// <summary>Starts the extra discussion clock once the operator confirms it — the counterpart to
    /// <see cref="StartDiscussionAsync"/> for the optional extra phase, only reachable from
    /// <see cref="PresentationStatus.ExtraDiscussionReady"/>.</summary>
    public async Task StartExtraDiscussionAsync(CancellationToken ct = default)
    {
        if (CurrentPresentation is null || Status != PresentationStatus.ExtraDiscussionReady)
        {
            return;
        }

        _timer.Start(ExtraDiscussionRemainingSeconds, TimerMode.ExtraDiscussion);
        await SetStatusAsync(PresentationStatus.ExtraDiscussion, HistoryEventType.DiscussionStarted,
            "Qo'shimcha muhokama vaqti boshlandi", ct);
    }

    /// <summary>Presentation time reaching zero moves straight to the discussion phase on its own — the
    /// operator never has to click a separate "move to discussion" button — but, like the manual Next path,
    /// the discussion clock itself still waits for an explicit <see cref="StartDiscussionAsync"/> call. The
    /// discussion clock running out mirrors the same pattern one phase later: it only auto-parks on
    /// <see cref="PresentationStatus.ExtraDiscussionReady"/> when the presentation actually has extra time
    /// configured — otherwise discussion expiry is left exactly as it behaved before extra time existed,
    /// stopped at zero with <see cref="Status"/> still <see cref="PresentationStatus.Discussion"/>.</summary>
    private async void OnTimerExpired(TimerMode expiredMode)
    {
        try
        {
            TimerExpired?.Invoke(expiredMode);

            if (expiredMode == TimerMode.Presentation && Status == PresentationStatus.Running)
            {
                await SetStatusAsync(PresentationStatus.DiscussionReady, HistoryEventType.NextPresenter,
                    "Muhokamaga o'tildi (boshlanishi kutilmoqda)", CancellationToken.None);
            }
            else if (expiredMode == TimerMode.Discussion && Status == PresentationStatus.Discussion
                && CurrentPresentation is { ExtraDiscussionTimeSeconds: > 0 })
            {
                await SetStatusAsync(PresentationStatus.ExtraDiscussionReady, HistoryEventType.NextPresenter,
                    "Qo'shimcha muhokamaga o'tildi (boshlanishi kutilmoqda)", CancellationToken.None);
            }
        }
        catch
        {
            // Timer callbacks run detached from any caller that could observe a fault; a failed DB write
            // here must not crash the whole app mid-presentation.
        }
    }

    private async Task AdvanceAsync(CancellationToken ct)
    {
        var previousPresentationId = CurrentPresentation?.Id;

        var next = _currentIndex + 1;
        while (next < _queue.Count && _queue[next].Status is PresentationStatus.Finished or PresentationStatus.Skipped)
        {
            next++;
        }
        // -1 means the queue is exhausted (everything remaining is Finished/Skipped); CurrentPresentation
        // then reports null and Status falls back to Waiting instead of resurrecting a finished entry.
        _currentIndex = next < _queue.Count ? next : -1;

        ResetTimersForCurrent();

        if (CurrentPresentation is null)
        {
            Status = PresentationStatus.Waiting;
        }
        else
        {
            // Start immediately rather than parking on Ready: the fullscreen Namoyish Ekrani has no Start
            // button of its own (that only lives on AdminForm), so leaving the next presentation at Ready
            // stranded the operator with nothing clickable — matches the class doc comment's stated flow
            // where Start (once, from AdminForm) and Next are the operator's only two actions.
            _timer.Start(PresentationRemainingSeconds, TimerMode.Presentation);
            Status = PresentationStatus.Running;
            CurrentPresentation.Status = PresentationStatus.Running;
            await _presentationRepository.UpdateAsync(CurrentPresentation, ct);
            await LogAsync(HistoryEventType.Started, $"Taqdimot boshlandi: {CurrentPresentation.Title}", ct);
        }

        await LogAsync(HistoryEventType.NextPresenter, previousPresentationId == CurrentPresentation?.Id
            ? "Navbatda harakat (o'zgarish yo'q)"
            : $"O'tildi: {CurrentPresentation?.Title ?? "(navbat tugadi)"}", ct);

        StatusChanged?.Invoke(Status);
        PresentationChanged?.Invoke();
    }

    private void ResetTimersForCurrent()
    {
        PresentationRemainingSeconds = CurrentPresentation?.PresentationTimeSeconds ?? 0;
        DiscussionRemainingSeconds = CurrentPresentation?.DiscussionTimeSeconds ?? 0;
        ExtraDiscussionRemainingSeconds = CurrentPresentation?.ExtraDiscussionTimeSeconds ?? 0;
    }

    private async Task SetStatusAsync(PresentationStatus status, HistoryEventType logEvent, string message, CancellationToken ct)
    {
        Status = status;
        await PersistStatusAsync(status, ct);
        await LogAsync(logEvent, message, ct);
        StatusChanged?.Invoke(status);
    }

    private async Task PersistStatusAsync(PresentationStatus status, CancellationToken ct)
    {
        if (CurrentPresentation is null)
        {
            return;
        }

        CurrentPresentation.Status = status;
        CurrentPresentation.UpdatedAt = DateTime.UtcNow;
        await _presentationRepository.UpdateAsync(CurrentPresentation, ct);
    }

    private async Task LogAsync(HistoryEventType eventType, string message, CancellationToken ct)
    {
        if (CurrentPresentation is null)
        {
            return;
        }

        await _historyRepository.AddAsync(new HistoryEntry
        {
            PresentationId = CurrentPresentation.Id,
            EventType = eventType,
            Message = message
        }, ct);
    }
}
