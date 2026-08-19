using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Models;

public sealed record AdminProjectCard(int ProjectId, string Name, string DateRange);

/// <summary>Drives the shared <c>_ProjectTabs</c> partial's active-tab highlight - <paramref name="Active"/>
/// is one of "participants"/"presentations"/"finalscores"/"criteria"/"judges"/"presenters".</summary>
public sealed record AdminProjectTabsViewModel(int ProjectId, string Active);

public sealed record AdminDashboardViewModel(string AdminFullName, IReadOnlyList<AdminProjectCard> Projects);

/// <summary>Reuses <see cref="ProjectParticipant"/> as-is - it's already exactly this row's shape (the same
/// type <see cref="ProjectService.GetParticipantsAsync"/> returns for the desktop Admin panel).</summary>
public sealed record AdminParticipantsViewModel(
    int ProjectId, string ProjectName, string? Query, IReadOnlyList<ProjectParticipant> Participants,
    DateTime? SubmissionDeadline);

public sealed record AdminPresentationRow(
    int PresentationId, string PresenterFullName, string Title, string StatusLabel, string CreatedAt,
    int ExtraDiscussionMinutes);

public sealed record AdminPresentationsViewModel(
    int ProjectId, string ProjectName, string? Query, IReadOnlyList<AdminPresentationRow> Presentations);

/// <summary>Reuses <see cref="EvaluationCriterion"/> (column headers) and <see cref="PresentationScoreSummary"/>
/// (rows) as-is - both already exactly match what the desktop Admin panel's "Yakuniy baholar" grid binds to.</summary>
public sealed record AdminFinalScoresViewModel(
    int ProjectId, string ProjectName, string? Query,
    IReadOnlyList<EvaluationCriterion> Criteria, IReadOnlyList<PresentationScoreSummary> Rows);

public sealed record AdminCriteriaViewModel(int ProjectId, string ProjectName, IReadOnlyList<EvaluationCriterion> Criteria);

/// <summary>Reuses <see cref="Judge"/> and <see cref="Presenter"/> as-is - <see cref="Candidates"/> is every
/// Telegram-registered presenter not already a judge for this project, mirroring
/// <c>JudgeManagementForm.OnAssignClick</c>'s own filter.</summary>
public sealed record AdminJudgesViewModel(
    int ProjectId, string ProjectName, IReadOnlyList<Judge> Judges, IReadOnlyList<Presenter> Candidates);

public sealed record AdminPresenterAssignmentRow(int AssignmentId, string FullName, string? PhoneNumber);

public sealed record AdminPresentersViewModel(
    int ProjectId, string ProjectName, IReadOnlyList<AdminPresenterAssignmentRow> Assigned, IReadOnlyList<Presenter> Candidates);
