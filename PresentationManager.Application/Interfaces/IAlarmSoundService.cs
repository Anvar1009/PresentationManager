namespace PresentationManager.Application.Interfaces;

public interface IAlarmSoundService
{
    void Play(string? customSoundPath);

    /// <summary>A quiet, fixed countdown tick — deliberately ignores the operator's custom alarm sound, since
    /// it's meant to read as a soft heads-up rather than the loud expiry alarm.</summary>
    void PlayTick();
}
