namespace PresentationManager.API.Models;

/// <summary>One project this judge is assigned to - <see cref="JudgeAssignmentId"/> is
/// <see cref="Domain.Entities.Judge.Id"/> (the project-assignment row, not the judge's own login), which is
/// what <see cref="Application.Services.ScoreService"/> keys scores on.</summary>
public sealed record JudgeProjectCard(int JudgeAssignmentId, int ProjectId, string ProjectName);

public sealed record JudgeDashboardViewModel(string JudgeFullName, IReadOnlyList<JudgeProjectCard> Projects);

public sealed record JudgePresentationRow(int PresentationId, string PresenterFullName, string Title, bool FullyScored);

public sealed record JudgeProjectViewModel(
    int JudgeAssignmentId, int ProjectId, string ProjectName, IReadOnlyList<JudgePresentationRow> Presentations);

public sealed record JudgeCriterionScore(int CriterionId, string CriterionName, int MaxScore, int? Value);

public sealed record JudgeEvaluateViewModel(
    int JudgeAssignmentId,
    int ProjectId,
    int PresentationId,
    string PresenterFullName,
    string PresentationTitle,
    IReadOnlyList<JudgeCriterionScore> Criteria);
