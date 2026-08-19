namespace PresentationManager.Application.Interfaces;

public interface IAlarmSoundService
{
    void Play(string? customSoundPath);

    /// <summary>The bundled "time's almost up" bell - fired once when a timer has 7 seconds left (see
    /// PresentationForm's own warning-state handling), not per-second like <see cref="PlayTick"/>. Always
    /// this specific sound, not the operator's configurable <see cref="Play"/> path - it's the app's own
    /// fixed countdown signal, not something meant to be swapped out.</summary>
    void PlayCountdownBell();
}
