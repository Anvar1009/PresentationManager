namespace PresentationManager.TelegramBot;

public enum AdminStep
{
    MainMenu,
    ProjectMenu,
    CreatingProjectName,
    CreatingProjectStartDate,
    CreatingProjectEndDate,
    CreatingProjectLocation,
    AddingCriterionName,
    AddingCriterionMaxScore,
    AssigningJudgePhone
}

/// <summary>Per-chat progress through an Admin's read/report + basic-management flow, once their chat is
/// linked to a desktop <see cref="PresentationManager.Domain.Entities.User"/> account (see
/// <see cref="PresentationManager.Application.Services.AdminLinkService"/>) — kept separate from
/// <see cref="ChatSession"/>/<see cref="JudgeSession"/> since a chat is only ever in one flow at a time but
/// the three shapes don't overlap.</summary>
public sealed class AdminSession
{
    public AdminStep Step { get; set; } = AdminStep.MainMenu;

    public int UserId { get; set; }

    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Scratch field for the multi-step "+ Yangi loyiha" flow - name, then start date, then end
    /// date, then location, each one prompted in its own message.</summary>
    public string NewProjectName { get; set; } = string.Empty;

    public DateOnly NewProjectStartDate { get; set; }

    public DateOnly NewProjectEndDate { get; set; }

    /// <summary>Scratch field for the "+ Mezon qo'shish" flow - name first, then max score.</summary>
    public string NewCriterionName { get; set; } = string.Empty;
}
