namespace PresentationManager.Domain.Entities;

/// <summary>One judging criterion for a project — e.g. "Mazmun" out of 10. Defined per-project by Admin
/// (dynamic, not a fixed global rubric) since different projects may judge on different things.</summary>
public class EvaluationCriterion
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public required string Name { get; set; }

    public int MaxScore { get; set; } = 10;

    public int OrderNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
