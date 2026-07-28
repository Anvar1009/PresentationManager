namespace PresentationManager.Domain.Enums;

public enum HistoryEventType
{
    Started = 0,
    Paused = 1,
    Resumed = 2,
    DiscussionStarted = 3,
    DiscussionPaused = 4,
    DiscussionResumed = 5,
    Finished = 6,
    Skipped = 7,
    NextPresenter = 8,
    PreviousPresenter = 9
}
