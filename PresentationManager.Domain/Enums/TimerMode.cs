namespace PresentationManager.Domain.Enums;

public enum TimerMode
{
    None = 0,
    Presentation = 1,
    Discussion = 2,

    /// <summary>The optional extra discussion time offered once <see cref="Discussion"/>'s own clock runs
    /// out — see <see cref="PresentationManager.Domain.Entities.Presentation.ExtraDiscussionTimeSeconds"/>.</summary>
    ExtraDiscussion = 3
}
