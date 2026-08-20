using System.Media;
using System.Reflection;
using NAudio.Wave;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.UI.Services;

/// <summary>Plays a custom .wav if the operator configured one in Settings, otherwise falls back to the
/// bundled countdown bell (<see cref="PlayCountdownBell"/>) - deliberately not an old Windows system beep
/// anymore, so an operator who never opened Settings still only ever hears this app's own sound, never a
/// generic OS ding layered on top of or instead of it. Lives here (not PresentationManager.Infrastructure)
/// since it's purely local audio playback with no database involved - unlike every other former
/// Infrastructure implementation, there is nothing here for PresentationManager.ApiClient to provide an
/// HTTP-backed alternative to.</summary>
public class AlarmSoundService : IAlarmSoundService
{
    /// <summary>Matches the embedded resource's auto-generated name (default MSBuild convention:
    /// {RootNamespace}.{folder path with '.'}.{file name}) for Sounds\timer-alarm.mp3 - see the
    /// EmbeddedResource entry in PresentationManager.UI.csproj.</summary>
    private const string CountdownBellResourceName = "PresentationManager.UI.Sounds.timer-alarm.mp3";

    private readonly object _lock = new();
    private SoundPlayer? _activeSoundPlayer;
    private WaveOutEvent? _activeWaveOut;

    public void Play(string? customSoundPath)
    {
        Stop();

        if (!string.IsNullOrWhiteSpace(customSoundPath) && File.Exists(customSoundPath))
        {
            try
            {
                // Not disposed immediately: Play() starts async playback on a background thread, and
                // disposing right away would cut the sound off before it finishes. Kept alive in
                // _activeSoundPlayer instead, so a later Stop() can still reach and silence it.
                var player = new SoundPlayer(customSoundPath);
                lock (_lock) { _activeSoundPlayer = player; }
                player.Play();
                return;
            }
            catch
            {
                // Falls through to the bundled bell below rather than a bare system beep.
            }
        }

        PlayCountdownBell();
    }

    public void PlayCountdownBell()
    {
        Stop();

        try
        {
            var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(CountdownBellResourceName);
            if (resourceStream is null)
            {
                SystemSounds.Exclamation.Play();
                return;
            }

            // SoundPlayer (used by Play() above) only understands .wav - NAudio is what actually decodes
            // this bundled .mp3. Both the reader and the output device are kept alive (not disposed until
            // PlaybackStopped fires) since Play() below starts async playback and returns immediately -
            // disposing right away would cut the bell off before it finishes ringing.
            var reader = new Mp3FileReader(resourceStream);
            var output = new WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += (_, _) =>
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_activeWaveOut, output)) _activeWaveOut = null;
                }
                output.Dispose();
                reader.Dispose();
            };

            lock (_lock) { _activeWaveOut = output; }
            output.Play();
        }
        catch
        {
            SystemSounds.Exclamation.Play();
        }
    }

    public void Stop()
    {
        SoundPlayer? soundPlayer;
        WaveOutEvent? waveOut;
        lock (_lock)
        {
            soundPlayer = _activeSoundPlayer;
            _activeSoundPlayer = null;
            waveOut = _activeWaveOut;
        }

        if (soundPlayer is not null)
        {
            try { soundPlayer.Stop(); } catch { }
            soundPlayer.Dispose();
        }

        // Triggers PlaybackStopped synchronously, which clears _activeWaveOut and disposes it/its reader -
        // no need to duplicate that cleanup here.
        waveOut?.Stop();
    }
}
