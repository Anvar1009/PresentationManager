using System.Media;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.UI.Services;

/// <summary>Plays a custom .wav if the operator configured one in Settings, otherwise falls back to a
/// built-in Windows system sound so the app never depends on a bundled audio asset. Lives here (not
/// PresentationManager.Infrastructure) since it's purely local audio playback with no database involved -
/// unlike every other former Infrastructure implementation, there is nothing here for
/// PresentationManager.ApiClient to provide an HTTP-backed alternative to.</summary>
public class AlarmSoundService : IAlarmSoundService
{
    public void Play(string? customSoundPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(customSoundPath) && File.Exists(customSoundPath))
            {
                // Not disposed immediately: Play() starts async playback on a background thread, and
                // disposing right away would cut the sound off before it finishes.
                new SoundPlayer(customSoundPath).Play();
            }
            else
            {
                SystemSounds.Exclamation.Play();
            }
        }
        catch
        {
            SystemSounds.Exclamation.Play();
        }
    }

    public void PlayTick()
    {
        try
        {
            // Asterisk reads as a soft "ding" next to Exclamation's harsher tone above — the closest thing
            // to a quieter signal available without a real per-instance volume API (SoundPlayer/SystemSounds
            // both always play at system volume).
            SystemSounds.Asterisk.Play();
        }
        catch
        {
            // Best-effort heads-up; losing it silently is fine.
        }
    }
}
