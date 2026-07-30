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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
