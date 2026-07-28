namespace PresentationManager.Domain.Enums;

public enum PresentationStatus
{
    Waiting = 0,
    Ready = 1,
    Running = 2,
    Paused = 3,
    Discussion = 4,
    DiscussionPaused = 5,
    Finished = 6,
    Skipped = 7,

    /// <summary>In the discussion phase but the discussion timer hasn't been started yet — reached either
    /// by clicking "Muhokamaga o'tish" early or by the presentation timer running out on its own. Appended
    /// at the end (not inserted near Discussion/DiscussionPaused) so existing persisted int values for
    /// every other status stay unchanged.</summary>
    DiscussionReady = 8
}
