namespace PresentationManager.Application.Interfaces;

public interface IAlarmSoundService
{
    void Play(string? customSoundPath);

    /// <summary>The bundled "time's almost up" bell - fired once when a timer has 7 seconds left (see
    /// PresentationForm's own warning-state handling). Always this specific sound, not the operator's
    /// configurable <see cref="Play"/> path - it's the app's own fixed countdown signal, not something
    /// meant to be swapped out.</summary>
    void PlayCountdownBell();

    /// <summary>Immediately silences whatever this service is currently playing, if anything. Called
    /// internally by <see cref="Play"/> and <see cref="PlayCountdownBell"/> before they start a new sound,
    /// so the countdown bell that started ringing near the end of the clock never keeps playing underneath
    /// the final alarm once the timer actually reaches 00:00.</summary>
    void Stop();
}
