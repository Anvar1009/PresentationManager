namespace PresentationManager.Domain.Entities;

public class Project
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>First day of the event — mandatory, unlike <see cref="EventTime"/>/<see cref="Location"/>,
    /// since "which day(s)" is the one detail a presenter always needs to know.</summary>
    public DateOnly EventStartDate { get; set; }

    /// <summary>Last day of the event — same as <see cref="EventStartDate"/> for a single-day event.</summary>
    public DateOnly EventEndDate { get; set; }

    public TimeOnly? EventTime { get; set; }

    public string? Location { get; set; }

    /// <summary>Admin-set cutoff (server/UTC time) past which the Telegram bot no longer accepts a new
    /// submission or a file/title update to an existing one for this project - null means no deadline, the
    /// current unrestricted behavior. Deliberately does not gate Admin's own manual queue management (adding/
    /// editing a presentation directly), only the presenter self-service bot flow - see
    /// <c>PresentationBotHostedService.HandleDocumentAsync</c>.</summary>
    public DateTime? SubmissionDeadline { get; set; }

    /// <summary>The Admin who created this project - null for projects created before this field existed, or
    /// created from the Operator's own "Loyihalar" dialog (<see cref="Enums.UserRole.Operator"/> accounts have
    /// no creator-scoped project list, unlike Admin's). Used to scope each Admin's "Loyihalar" dropdown to
    /// only the projects they created - see <c>IProjectRepository.GetByCreatorAsync</c>.</summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>When the OrderOperator's "Ro'yxatni shakllantirish" last actually ran for this project - null
    /// means it never has (or "Jadvalni tozalash" undid it back to a rehearsal state). Purely a status flag
    /// for the Order dashboard's "Tartiblangan" badge (see OrderController.Dashboard) - the real order itself
    /// always lives in each Presentation's own OrderNumber regardless of this field.</summary>
    public DateTime? OrderRandomizedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
