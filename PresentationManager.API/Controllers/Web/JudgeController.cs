using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Models;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Controllers.Web;

/// <summary>The Judge web platform's only real screens - a project picker, a per-project presentation list,
/// and a per-presentation scoring form. Mirrors what the Telegram bot's (now removed) in-chat judge flow used
/// to do, reusing the exact same Application-layer services (<see cref="JudgeService"/>,
/// <see cref="ScoreService"/>, <see cref="CriterionService"/>) - only the UI changed.</summary>
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Roles = nameof(UserRole.Judge))]
public sealed class JudgeController : Controller
{
    private readonly JudgeService _judgeService;
    private readonly ProjectService _projectService;
    private readonly PresentationQueueService _queueService;
    private readonly CriterionService _criterionService;
    private readonly ScoreService _scoreService;

    public JudgeController(
        JudgeService judgeService, ProjectService projectService, PresentationQueueService queueService,
        CriterionService criterionService, ScoreService scoreService)
    {
        _judgeService = judgeService;
        _projectService = projectService;
        _queueService = queueService;
        _criterionService = criterionService;
        _scoreService = scoreService;
    }

    /// <summary>Null only if this Judge account somehow has no TelegramChatId - see UserRole.Judge's own
    /// doc comment for why every account created through the intended flow always has one.</summary>
    private long? CurrentTelegramChatId => long.TryParse(User.FindFirst("TelegramChatId")?.Value, out var id) ? id : null;

    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var assignments = await GetAssignmentsAsync(ct);
        var projects = await _projectService.GetAllAsync(ct);
        var projectsById = projects.ToDictionary(p => p.Id);

        var cards = assignments
            .Select(a => new JudgeProjectCard(a.Id, a.ProjectId, projectsById.TryGetValue(a.ProjectId, out var p) ? p.Name : "?"))
            .ToList();

        return View(new JudgeDashboardViewModel(User.Identity?.Name ?? string.Empty, cards));
    }

    public async Task<IActionResult> Project(int judgeAssignmentId, CancellationToken ct)
    {
        var assignment = await FindAssignmentAsync(judgeAssignmentId, ct);
        if (assignment is null)
        {
            return Forbid();
        }

        var projects = await _projectService.GetAllAsync(ct);
        var projectName = projects.FirstOrDefault(p => p.Id == assignment.ProjectId)?.Name ?? "?";

        var presentations = await _queueService.GetAllAsync(assignment.ProjectId, ct);
        var criteria = await _criterionService.GetByProjectIdAsync(assignment.ProjectId, ct);

        var rows = new List<JudgePresentationRow>();
        foreach (var presentation in presentations)
        {
            var progress = await _scoreService.GetJudgeProgressAsync(presentation.Id, assignment.Id, ct);
            var fullyScored = criteria.Count > 0 && criteria.All(c => progress.ContainsKey(c.Id));
            rows.Add(new JudgePresentationRow(presentation.Id, presentation.FullName, presentation.Title, fullyScored));
        }

        return View(new JudgeProjectViewModel(assignment.Id, assignment.ProjectId, projectName, rows));
    }

    public async Task<IActionResult> Evaluate(int judgeAssignmentId, int presentationId, CancellationToken ct)
    {
        var assignment = await FindAssignmentAsync(judgeAssignmentId, ct);
        if (assignment is null)
        {
            return Forbid();
        }

        var presentation = await FindPresentationAsync(assignment.ProjectId, presentationId, ct);
        if (presentation is null)
        {
            return NotFound();
        }

        var criteria = await _criterionService.GetByProjectIdAsync(assignment.ProjectId, ct);
        var progress = await _scoreService.GetJudgeProgressAsync(presentationId, assignment.Id, ct);

        var scores = criteria
            .Select(c => new JudgeCriterionScore(c.Id, c.Name, c.MaxScore, progress.TryGetValue(c.Id, out var v) ? v : null))
            .ToList();

        return View(new JudgeEvaluateViewModel(
            assignment.Id, assignment.ProjectId, presentation.Id, presentation.FullName, presentation.Title, scores));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitScore(int judgeAssignmentId, int presentationId, int criterionId, int value, CancellationToken ct)
    {
        var assignment = await FindAssignmentAsync(judgeAssignmentId, ct);
        if (assignment is null)
        {
            return Forbid();
        }

        try
        {
            await _scoreService.UpsertAsync(presentationId, assignment.Id, criterionId, value, ct);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Evaluate), new { judgeAssignmentId, presentationId });
    }

    private async Task<List<Judge>> GetAssignmentsAsync(CancellationToken ct) =>
        CurrentTelegramChatId is { } chatId ? await _judgeService.GetLinkedAssignmentsByChatIdAsync(chatId, ct) : [];

    /// <summary>Confirms <paramref name="judgeAssignmentId"/> actually belongs to the logged-in judge before
    /// any read/write against it - every action above takes this as a route/form value rather than trusting
    /// a project/presentation id alone, since Judge.Id is what scores are keyed on and a stale or tampered
    /// id must never let one judge see or overwrite another's assignment.</summary>
    private async Task<Judge?> FindAssignmentAsync(int judgeAssignmentId, CancellationToken ct)
    {
        var assignments = await GetAssignmentsAsync(ct);
        return assignments.FirstOrDefault(a => a.Id == judgeAssignmentId);
    }

    private async Task<Presentation?> FindPresentationAsync(int projectId, int presentationId, CancellationToken ct)
    {
        var presentations = await _queueService.GetAllAsync(projectId, ct);
        return presentations.FirstOrDefault(p => p.Id == presentationId);
    }
}
