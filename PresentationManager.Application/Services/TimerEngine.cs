using PresentationManager.Domain.Enums;

namespace PresentationManager.Application.Services;

/// <summary>
/// Framework-agnostic 1-second countdown ticker. Only one channel (presentation or discussion) is ever
/// active at a time; <see cref="PresentationSessionController"/> owns the frozen remaining-seconds values
/// for the inactive channel and decides which one to feed in on Start.
/// </summary>
public sealed class TimerEngine : IDisposable
{
    private System.Threading.Timer? _timer;
    private SynchronizationContext? _syncContext;
    private volatile bool _isRunning;
    private int _remainingSeconds;

    public event Action<int>? Tick;
    public event Action? Expired;

    public TimerMode Mode { get; private set; } = TimerMode.None;

    public int RemainingSeconds => _remainingSeconds;

    /// <summary>Starts ticking down from <paramref name="remainingSeconds"/> — never resets on its own, so
    /// callers must pass the frozen value when resuming, not the configured default.</summary>
    public void Start(int remainingSeconds, TimerMode mode)
    {
        // Captured here (not in the constructor) so it works regardless of whether this engine was
        // constructed before or after the WinForms message loop installed its SynchronizationContext.
        _syncContext ??= SynchronizationContext.Current;

        Mode = mode;
        _remainingSeconds = remainingSeconds;
        _isRunning = true;

        _timer?.Dispose();
        _timer = new System.Threading.Timer(OnTick, null, 1000, 1000);
        RaiseTick();
    }

    /// <summary>Freezes the countdown; <see cref="RemainingSeconds"/> keeps its current value.</summary>
    public void Pause()
    {
        _isRunning = false;
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Stops and clears the mode entirely (used on Finish/Next, not on Pause).</summary>
    public void Stop()
    {
        _isRunning = false;
        Mode = TimerMode.None;
        _remainingSeconds = 0;
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTick(object? state)
    {
        if (!_isRunning)
        {
            return;
        }

        _remainingSeconds = Math.Max(0, _remainingSeconds - 1);
        RaiseTick();

        if (_remainingSeconds == 0)
        {
            _isRunning = false;
            _timer?.Dispose();
            _timer = null;
            RaiseExpired();
        }
    }

    private void RaiseTick() => Marshal(() => Tick?.Invoke(_remainingSeconds));

    private void RaiseExpired() => Marshal(() => Expired?.Invoke());

    private void Marshal(Action action)
    {
        if (_syncContext is not null)
        {
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    public void Dispose() => _timer?.Dispose();
}
