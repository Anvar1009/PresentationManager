namespace PresentationManager.Domain.Entities;

/// <summary>One judge's score for one presentation against one criterion. A judge can revise their own
/// score (upsert on the (PresentationId, JudgeId, CriterionId) triple) but never sees or is influenced by
/// other judges' scores through the bot.</summary>
public class Score
{
    public int Id { get; set; }

    public int PresentationId { get; set; }

    public int JudgeId { get; set; }

    public int CriterionId { get; set; }

    public int Value { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
