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
    DiscussionReady = 8,

    /// <summary>Mirrors <see cref="DiscussionReady"/> one phase later: the normal discussion clock just ran
    /// out and the presentation has extra discussion time configured, but that extra clock hasn't been
    /// started yet — parked here until the operator confirms. Only ever reached when
    /// <see cref="PresentationManager.Domain.Entities.Presentation.ExtraDiscussionTimeSeconds"/> is greater
    /// than zero; otherwise discussion expiry behaves exactly as it did before this status existed.</summary>
    ExtraDiscussionReady = 9,

    /// <summary>The extra discussion clock is actually ticking — the counterpart to <see cref="Discussion"/>
    /// for the optional extra phase.</summary>
    ExtraDiscussion = 10
}
