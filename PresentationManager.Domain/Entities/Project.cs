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

    /// <summary>The Admin who created this project - null for projects created before this field existed, or
    /// created from the Operator's own "Loyihalar" dialog (<see cref="Enums.UserRole.Operator"/> accounts have
    /// no creator-scoped project list, unlike Admin's). Used to scope each Admin's "Loyihalar" dropdown to
    /// only the projects they created - see <c>IProjectRepository.GetByCreatorAsync</c>.</summary>
    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
