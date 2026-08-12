using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient.Wire;

/// <summary>Mirrors PresentationManager.API.Dtos.SettingsDto.</summary>
internal sealed record SettingsWire(
    int DefaultPresentationTimeSeconds, int DefaultDiscussionTimeSeconds, bool AutoNext, string Theme,
    bool FullscreenEnabled, string? AlarmSoundPath, bool AlarmEnabled, string StorageFolderPath,
    int? LastActiveProjectId)
{
    public static SettingsWire FromEntity(AppSettings s) => new(
        s.DefaultPresentationTimeSeconds, s.DefaultDiscussionTimeSeconds, s.AutoNext, s.Theme,
        s.FullscreenEnabled, s.AlarmSoundPath, s.AlarmEnabled, s.StorageFolderPath, s.LastActiveProjectId);

    public AppSettings ToEntity() => new()
    {
        Id = 1,
        DefaultPresentationTimeSeconds = DefaultPresentationTimeSeconds,
        DefaultDiscussionTimeSeconds = DefaultDiscussionTimeSeconds,
        AutoNext = AutoNext,
        Theme = Theme,
        FullscreenEnabled = FullscreenEnabled,
        AlarmSoundPath = AlarmSoundPath,
        AlarmEnabled = AlarmEnabled,
        StorageFolderPath = StorageFolderPath,
        LastActiveProjectId = LastActiveProjectId
    };
}
