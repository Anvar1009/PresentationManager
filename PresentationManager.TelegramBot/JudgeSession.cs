namespace PresentationManager.TelegramBot;

public enum JudgeStep
{
    SelectingProject,
    SelectingPresentation,

    /// <summary>A presentation is picked and the criteria/score buttons are being used — everything from
    /// here on is button-driven (see <c>PresentationBotHostedService</c>'s jcrit:/jscore: callbacks), so no
    /// further sub-states are needed; this just tells stray text messages to be redirected to the buttons.</summary>
    ScoringPresentation
}

/// <summary>Per-chat progress through a judge's scoring flow — kept separate from <see cref="ChatSession"/>
/// (the presenter upload flow) since a chat is only ever in one of the two at a time, but the two shapes
/// don't otherwise overlap.</summary>
public sealed class JudgeSession
{
    public JudgeStep Step { get; set; } = JudgeStep.SelectingProject;

    public int JudgeId { get; set; }

    public int ProjectId { get; set; }

    public int PresentationId { get; set; }
}
