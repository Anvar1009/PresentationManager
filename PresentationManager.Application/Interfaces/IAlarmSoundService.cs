namespace PresentationManager.Application.Interfaces;

public interface IAlarmSoundService
{
    void Play(string? customSoundPath);

    /// <summary>The bundled "time's almost up" bell - <see cref="Play"/>'s own fallback when no custom sound
    /// is configured. Always this specific sound, not the operator's configurable path - it's the app's own
    /// fixed countdown signal, not something meant to be swapped out.</summary>
    void PlayCountdownBell();

    /// <summary>Immediately silences whatever this service is currently playing, if anything. Called
    /// internally by <see cref="Play"/> and <see cref="PlayCountdownBell"/> before they start a new sound,
    /// and by PresentationForm the instant a timer reaches 00:00 - the alarm starts a few seconds early (see
    /// PresentationForm's own warning-state handling) and must never keep ringing past expiry regardless of
    /// how long the underlying sound file actually runs.</summary>
    void Stop();
}
