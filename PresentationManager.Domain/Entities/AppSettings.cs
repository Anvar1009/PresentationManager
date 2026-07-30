namespace PresentationManager.Domain.Entities;

/// <summary>Single-row table (Id is always 1) holding global app configuration.</summary>
public class AppSettings
{
    public int Id { get; set; } = 1;

    public int DefaultPresentationTimeSeconds { get; set; } = 180;

    public int DefaultDiscussionTimeSeconds { get; set; } = 120;

    public bool AutoNext { get; set; } = false;

    public string Theme { get; set; } = "Dark";

    public bool FullscreenEnabled { get; set; } = true;

    public string? AlarmSoundPath { get; set; }

    public bool AlarmEnabled { get; set; } = true;

    public string StorageFolderPath { get; set; } = "Files";

    /// <summary>Id of the project the operator had active when the app was last closed, so it's
    /// automatically re-selected on next launch instead of forcing a re-pick every time.</summary>
    public int? LastActiveProjectId { get; set; }
}
