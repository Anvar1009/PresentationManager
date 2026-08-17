using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Models;
using PresentationManager.API.Services;
using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.TelegramBot;

namespace PresentationManager.API.Controllers.Web;

/// <summary>Web mirror of the WinForms <c>AdminPanelForm</c> - project picker, Qatnashchilar/Taqdimotlar/
/// Yakuniy baholar (all scoped to this Admin's own projects, see <see cref="FindOwnedProjectAsync"/>), and a
/// project's Baholash mezonlari/Hakamlar/Ishtirokchilar management. Deliberately does not add
/// presentation upload/edit/delete - <c>AdminPanelForm</c>'s own "Taqdimotlar" section is read-only + open-
/// file only; full presentation CRUD only exists in the separate Operator-only <c>PresentationManagementForm</c>,
/// which has no web equivalent (out of scope here). Every action reuses the same Application-layer services
/// the desktop panel already calls - no new business logic, only a new presentation layer.</summary>
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Roles = nameof(UserRole.Admin))]
public sealed class AdminController : Controller
{
    private readonly ProjectService _projectService;
    private readonly PresentationQueueService _queueService;
    private readonly CriterionService _criterionService;
    private readonly JudgeService _judgeService;
    private readonly PresenterAssignmentService _assignmentService;
    private readonly ScoreService _scoreService;
    private readonly IPresenterRepository _presenterRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IPresentationRepository _presentationRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly TelegramNotifier _telegramNotifier;

    public AdminController(
        ProjectService projectService, PresentationQueueService queueService, CriterionService criterionService,
        JudgeService judgeService, PresenterAssignmentService assignmentService, ScoreService scoreService,
        IPresenterRepository presenterRepository, IProjectRepository projectRepository,
        IPresentationRepository presentationRepository, IFileStorageService fileStorageService,
        TelegramNotifier telegramNotifier)
    {
        _projectService = projectService;
        _queueService = queueService;
        _criterionService = criterionService;
        _judgeService = judgeService;
        _assignmentService = assignmentService;
        _scoreService = scoreService;
        _presenterRepository = presenterRepository;
        _projectRepository = projectRepository;
        _presentationRepository = presentationRepository;
        _fileStorageService = fileStorageService;
        _telegramNotifier = telegramNotifier;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var projects = await _projectService.GetByCreatorAsync(CurrentUserId, ct);
        var cards = projects
            .Select(p => new AdminProjectCard(p.Id, p.Name, FormatDateRange(p)))
            .ToList();

        return View(new AdminDashboardViewModel(User.Identity?.Name ?? string.Empty, cards));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProject(string name, DateOnly eventStartDate, DateOnly eventEndDate, TimeOnly? eventTime, string? location, CancellationToken ct)
    {
        try
        {
            await _projectService.CreateAsync(name, eventStartDate, eventEndDate, eventTime, location, CurrentUserId, ct);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Dashboard));
    }

    public IActionResult Project(int projectId) => RedirectToAction(nameof(Participants), new { projectId });

    public async Task<IActionResult> Participants(int projectId, string? q, CancellationToken ct)
    {
        var project = await FindOwnedProjectAsync(projectId, ct);
        if (project is null)
        {
            return NotFound();
        }

        var participants = await _projectService.GetParticipantsAsync(projectId, ct);
        if (!string.IsNullOrWhiteSpace(q))
        {
            participants = participants
                .Where(p => p.FullName.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (p.PhoneNumber?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return View(new AdminParticipantsViewModel(project.Id, project.Name, q, participants));
    }

    public async Task<IActionResult> Presentations(int projectId, string? q, CancellationToken ct)
    {
        var project = await FindOwnedProjectAsync(projectId, ct);
        if (project is null)
        {
            return NotFound();
        }

        var presentations = string.IsNullOrWhiteSpace(q)
            ? await _queueService.GetAllAsync(projectId, ct)
            : await _queueService.SearchAsync(q, projectId, ct);

        var rows = presentations
            .Select(p => new AdminPresentationRow(p.Id, p.FullName, p.Title, UzbekText.StatusLabel(p.Status), p.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")))
            .ToList();

        return View(new AdminPresentationsViewModel(project.Id, project.Name, q, rows));
    }

    /// <summary>Web equivalent of the desktop panel's "Faylni ochish" (native open) - a server process can't
    /// launch a client-side viewer, so a download is the correct web analog.</summary>
    public async Task<IActionResult> DownloadPresentation(int presentationId, CancellationToken ct)
    {
        var presentation = await _presentationRepository.GetByIdAsync(presentationId, ct);
        if (presentation is null || await FindOwnedProjectAsync(presentation.ProjectId, ct) is null)
        {
            return NotFound();
        }

        var absolutePath = await _fileStorageService.GetAbsolutePathAsync(presentation.FilePath, ct);
        if (!System.IO.File.Exists(absolutePath))
        {
            return NotFound();
        }

        var fileName = $"{presentation.FullName} - {presentation.Title}{Path.GetExtension(absolutePath)}";
        return PhysicalFile(absolutePath, "application/octet-stream", fileName);
    }

    public async Task<IActionResult> FinalScores(int projectId, string? q, CancellationToken ct)
    {
        var project = await FindOwnedProjectAsync(projectId, ct);
        if (project is null)
        {
            return NotFound();
        }

        var criteria = await _criterionService.GetByProjectIdAsync(projectId, ct);
        var rows = await _scoreService.GetFinalScoresAsync(projectId, ct);
        if (!string.IsNullOrWhiteSpace(q))
        {
            rows = rows
                .Where(r => r.PresenterFullName.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return View(new AdminFinalScoresViewModel(project.Id, project.Name, q, criteria, rows));
    }

    public async Task<IActionResult> ExportFinalScores(int projectId, CancellationToken ct)
    {
        var project = await FindOwnedProjectAsync(projectId, ct);
        if (project is null)
        {
            return NotFound();
        }

        var criteria = await _criterionService.GetByProjectIdAsync(projectId, ct);
        var rows = await _scoreService.GetFinalScoresAsync(projectId, ct);
        var bytes = FinalScoresExcelExporter.Export(project.Name, criteria, rows);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "yakuniy-baholar.xlsx");
    }

    public async Task<IActionResult> Criteria(int projectId, CancellationToken ct)
    {
        var project = await FindOwnedProjectAsync(projectId, ct);
        if (project is null)
        {
            return NotFound();
        }

        var criteria = await _criterionService.GetByProjectIdAsync(projectId, ct);
        return View(new AdminCriteriaViewModel(project.Id, project.Name, criteria));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCriterion(int projectId, string name, int maxScore, CancellationToken ct)
    {
        if (await FindOwnedProjectAsync(projectId, ct) is null)
        {
            return NotFound();
        }

        try
        {
            await _criterionService.CreateAsync(projectId, name, maxScore, ct);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Criteria), new { projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCriterion(int id, int projectId, CancellationToken ct)
    {
        if (await FindOwnedProjectAsync(projectId, ct) is null)
        {
            return NotFound();
        }

        await _criterionService.DeleteAsync(id, ct);
        return RedirectToAction(nameof(Criteria), new { projectId });
    }

    public async Task<IActionResult> Judges(int projectId, CancellationToken ct)
    {
        var project = await FindOwnedProjectAsync(projectId, ct);
        if (project is null)
        {
            return NotFound();
        }

        var judges = await _judgeService.GetByProjectIdAsync(projectId, ct);
        var candidates = await GetJudgeCandidatesAsync(projectId, ct);

        return View(new AdminJudgesViewModel(project.Id, project.Name, judges, candidates));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignJudge(int projectId, int presenterId, CancellationToken ct)
    {
        if (await FindOwnedProjectAsync(projectId, ct) is null)
        {
            return NotFound();
        }

        try
        {
            var judge = await _judgeService.AssignAsync(projectId, presenterId, ct);

            // Same server-side push as JudgesController.Add - the JudgeService.JudgeAssigned C# event has no
            // subscriber in this process (only that JSON API controller currently notifies, by calling the
            // repository directly rather than the event), so this in-process call needs its own notify block.
            if (judge.TelegramChatId is { } chatId)
            {
                var project = await _projectRepository.GetByIdAsync(judge.ProjectId, ct);
                var projectName = project?.Name ?? "loyiha";
                await _telegramNotifier.TrySendMessageAsync(chatId,
                    $"🎉 Tabriklaymiz! Siz \"{projectName}\" loyihasi uchun hakam etib tayinlandingiz.\nPastdagi \"📋 Loyihalar\" tugmasi orqali istalgan vaqt boshlashingiz mumkin.",
                    PresentationBotHostedService.JudgeMainKeyboard, ct);
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Judges), new { projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteJudge(int id, int projectId, CancellationToken ct)
    {
        if (await FindOwnedProjectAsync(projectId, ct) is null)
        {
            return NotFound();
        }

        await _judgeService.DeleteAsync(id, ct);
        return RedirectToAction(nameof(Judges), new { projectId });
    }

    public async Task<IActionResult> Presenters(int projectId, CancellationToken ct)
    {
        var project = await FindOwnedProjectAsync(projectId, ct);
        if (project is null)
        {
            return NotFound();
        }

        var assignments = await _assignmentService.GetByProjectIdAsync(projectId, ct);
        var allPresenters = await _presenterRepository.GetAllAsync(ct);
        var presentersById = allPresenters.ToDictionary(p => p.Id);

        var assigned = assignments
            .Select(a => new AdminPresenterAssignmentRow(
                a.Id,
                presentersById.TryGetValue(a.PresenterId, out var presenter) ? presenter.FullName : "?",
                presentersById.TryGetValue(a.PresenterId, out var p2) ? p2.PhoneNumber : null))
            .ToList();

        var assignedPresenterIds = assignments.Select(a => a.PresenterId).ToHashSet();
        var candidates = allPresenters.Where(p => !assignedPresenterIds.Contains(p.Id)).ToList();

        return View(new AdminPresentersViewModel(project.Id, project.Name, assigned, candidates));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignPresenter(int projectId, int presenterId, CancellationToken ct)
    {
        if (await FindOwnedProjectAsync(projectId, ct) is null)
        {
            return NotFound();
        }

        try
        {
            var assignment = await _assignmentService.AssignAsync(projectId, presenterId, ct);

            // Same server-side push as PresenterAssignmentsController.Add - see AssignJudge's own remark
            // above for why this in-process call needs to send the notification itself. Includes the event
            // reminder and docks PresenterMainKeyboard - see that controller's matching block for why.
            var presenter = await _presenterRepository.GetByIdAsync(assignment.PresenterId, ct);
            if (presenter?.TelegramChatId is { } chatId)
            {
                var project = await _projectRepository.GetByIdAsync(assignment.ProjectId, ct);
                var projectName = project?.Name ?? "loyiha";
                var message = $"🎉 Tabriklaymiz! Siz \"{projectName}\" loyihasida ishtirok etish uchun tasdiqlandingiz.\nEndi taqdimotingizni yuborishingiz mumkin - buning uchun pastdagi \"📤 Taqdimot jo'natish\" tugmasini bosing.";
                if (project is not null)
                {
                    message += $"\n\n{EventReminderFormatter.Format(project)}";
                }

                await _telegramNotifier.TrySendMessageAsync(chatId, message, PresentationBotHostedService.PresenterMainKeyboard, ct);
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Presenters), new { projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePresenterAssignment(int id, int projectId, CancellationToken ct)
    {
        if (await FindOwnedProjectAsync(projectId, ct) is null)
        {
            return NotFound();
        }

        await _assignmentService.DeleteAsync(id, ct);
        return RedirectToAction(nameof(Presenters), new { projectId });
    }

    /// <summary>Confirms <paramref name="projectId"/> actually belongs to (or predates) this logged-in
    /// Admin before any read/write against it - same defensive scoping <c>JudgeController.FindAssignmentAsync</c>
    /// applies to judge assignments, so one Admin can never reach another Admin's project by guessing an id in
    /// the URL. A null <see cref="Project.CreatedByUserId"/> (legacy rows from before this field existed) is
    /// treated as visible to every Admin, matching <see cref="ProjectService.GetByCreatorAsync"/>'s own
    /// allowance.</summary>
    private async Task<Project?> FindOwnedProjectAsync(int projectId, CancellationToken ct)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, ct);
        if (project is null)
        {
            return null;
        }

        return project.CreatedByUserId is int creatorId && creatorId != CurrentUserId ? null : project;
    }

    private async Task<List<Presenter>> GetJudgeCandidatesAsync(int projectId, CancellationToken ct)
    {
        var allPresenters = await _presenterRepository.GetAllAsync(ct);
        var existingJudges = await _judgeService.GetByProjectIdAsync(projectId, ct);
        var existingChatIds = existingJudges.Select(j => j.TelegramChatId).ToHashSet();
        return allPresenters.Where(p => !existingChatIds.Contains(p.TelegramChatId)).ToList();
    }

    private static string FormatDateRange(Project project) =>
        project.EventStartDate == project.EventEndDate
            ? project.EventStartDate.ToString("dd.MM.yyyy")
            : $"{project.EventStartDate:dd.MM.yyyy} - {project.EventEndDate:dd.MM.yyyy}";
}
