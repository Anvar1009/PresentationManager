namespace PresentationManager.Application.Services;

/// <summary>One row of a project's final scores table — one per presentation, with each criterion's average
/// across every judge who scored it, plus the sum of those averages as the overall total.</summary>
public sealed class PresentationScoreSummary
{
    public required int PresentationId { get; init; }

    public required string PresenterFullName { get; init; }

    public required string Title { get; init; }

    public required Dictionary<int, double> AverageByCriterionId { get; init; }

    public required double Total { get; init; }
}
