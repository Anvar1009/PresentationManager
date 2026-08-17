using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Models;
using PresentationManager.API.Services;
using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Controllers.Web;

/// <summary>Web mirror of the WinForms <c>SuperAdminPanelForm</c> - every project/presentation/presenter/
/// judge/score/jurnal list is read-only, matching the desktop panel's own "faqat ko'rish rejimi" (except
/// Foydalanuvchilar, the one section with real CRUD). <see cref="Dashboard"/> is new: a stat-card overview
/// instead of landing straight on the Loyihalar list, agreed as part of bringing this panel to the web.</summary>
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Roles = nameof(UserRole.SuperAdmin))]
public sealed class SuperAdminController : Controller
{
    private readonly ProjectService _projectService;
    private readonly PresentationQueueService _queueService;
    private readonly UserService _userService;
    private readonly JudgeService _judgeService;
    private readonly CriterionService _criterionService;
    private readonly ScoreService _scoreService;
    private readonly IPresenterRepository _presenterRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IPresentationRepository _presentationRepository;
    private readonly IHistoryRepository _historyRepository;
    private readonly IFileStorageService _fileStorageService;

    public SuperAdminController(
        ProjectService projectService, PresentationQueueService queueService, UserService userService,
        JudgeService judgeService, CriterionService criterionService, ScoreService scoreService,
        IPresenterRepository presenterRepository, IProjectRepository projectRepository,
        IPresentationRepository presentationRepository, IHistoryRepository historyRepository,
        IFileStorageService fileStorageService)
    {
        _projectService = projectService;
        _queueService = queueService;
        _userService = userService;
        _judgeService = judgeService;
        _criterionService = criterionService;
        _scoreService = scoreService;
        _presenterRepository = presenterRepository;
        _projectRepository = projectRepository;
        _presentationRepository = presentationRepository;
        _historyRepository = historyRepository;
        _fileStorageService = fileStorageService;
    }

    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var projects = await _projectService.GetAllAsync(ct);
        var presenters = await _presenterRepository.GetAllAsync(ct);
        var users = await _userService.GetAllAsync(ct);
        var judges = await _judgeService.GetAllAsync(ct);
        var presentations = await _queueService.GetAllAsync(ct);

        var byStatus = presentations
            .GroupBy(p => p.Status)
            .Select(g => new SuperAdminStatusCount(
                UzbekText.StatusLabel(g.Key), g.Count(),
                presentations.Count == 0 ? 0 : g.Count() * 100.0 / presentations.Count))
            .OrderByDescending(s => s.Count)
            .ToList();

        // Ranked by presentation count rather than a fabricated "project status" - a project itself has no
        // single lifecycle state in this domain (see SuperAdminTopProjectRow's doc comment), unlike one of
        // its individual presentations.
        var topProjects = presentations
            .GroupBy(p => p.ProjectId)
            .Select(g => new
            {
                Project = projects.FirstOrDefault(p => p.Id == g.Key),
                PresentationCount = g.Count(),
                PresenterCount = g.Select(p => p.FullName).Distinct().Count()
            })
            .Where(x => x.Project is not null)
            .OrderByDescending(x => x.PresentationCount)
            .Take(5)
            .Select(x => new SuperAdminTopProjectRow(
                x.Project!.Id, x.Project.Name, x.PresenterCount, x.PresentationCount,
                $"{x.Project.EventStartDate:dd.MM.yyyy} - {x.Project.EventEndDate:dd.MM.yyyy}"))
            .ToList();

        var recentActivity = (await _historyRepository.GetRecentAsync(5, ct))
            .Select(entry => new SuperAdminActivityRow(entry.Message, entry.EventType, FormatRecentTimestamp(entry.Timestamp)))
            .ToList();

        return View(new SuperAdminDashboardViewModel(
            User.Identity?.Name ?? string.Empty,
            projects.Count, presenters.Count, users.Count, judges.Count, presentations.Count,
            byStatus, topProjects, recentActivity));
    }

    /// <summary>"Bugun, HH:mm" / "Kecha, HH:mm" for anything from the last two calendar days, falling back to
    /// a full date beyond that - matches how a Jurnal entry actually gets read at a glance on the dashboard
    /// (which only ever shows the most recent handful), unlike the full "Jurnal" tab's plain timestamp column
    /// where every row needs an unambiguous absolute date regardless of age.</summary>
    private static string FormatRecentTimestamp(DateTime utcTimestamp)
    {
        var local = utcTimestamp.ToLocalTime();
        var today = DateTime.Now.Date;
        return local.Date == today ? $"Bugun, {local:HH:mm}"
            : local.Date == today.AddDays(-1) ? $"Kecha, {local:HH:mm}"
            : local.ToString("dd.MM.yyyy HH:mm");
    }

    public async Task<IActionResult> Projects(string? q, CancellationToken ct)
    {
        var projects = await _projectService.GetAllAsync(ct);
        if (!string.IsNullOrWhiteSpace(q))
        {
            projects = projects.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return View(new SuperAdminProjectsViewModel(q, projects));
    }

    public async Task<IActionResult> Presentations(string? q, CancellationToken ct)
    {
        var presentations = await _queueService.GetAllAsync(ct);
        var projectNames = (await _projectService.GetAllAsync(ct)).ToDictionary(p => p.Id, p => p.Name);

        var rows = presentations
            .Select(p => new SuperAdminPresentationRow(
                p.Id, projectNames.GetValueOrDefault(p.ProjectId, "?"), p.FullName, p.Title,
                UzbekText.StatusLabel(p.Status), p.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")))
            .ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            rows = rows
                .Where(r => r.PresenterFullName.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.ProjectName.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return View(new SuperAdminPresentationsViewModel(q, rows));
    }

    /// <summary>SuperAdmin isn't project-owner scoped (unlike <c>AdminController.DownloadPresentation</c>),
    /// so this only needs to confirm the presentation itself exists.</summary>
    public async Task<IActionResult> DownloadPresentation(int presentationId, CancellationToken ct)
    {
        var presentation = await _presentationRepository.GetByIdAsync(presentationId, ct);
        if (presentation is null)
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

    public async Task<IActionResult> Presenters(string? q, CancellationToken ct)
    {
        var presenters = await _presenterRepository.GetAllAsync(ct);
        if (!string.IsNullOrWhiteSpace(q))
        {
            presenters = presenters
                .Where(p => p.FullName.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (p.PhoneNumber?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return View(new SuperAdminPresentersViewModel(q, presenters));
    }

    public async Task<IActionResult> Judges(string? q, CancellationToken ct)
    {
        var judges = await _judgeService.GetAllAsync(ct);
        var projectNames = (await _projectService.GetAllAsync(ct)).ToDictionary(p => p.Id, p => p.Name);

        var rows = judges
            .Select(j => new SuperAdminJudgeRow(j.Id, projectNames.GetValueOrDefault(j.ProjectId, "?"), j.FullName ?? "(ism yo'q)", j.PhoneNumber))
            .ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            rows = rows
                .Where(r => r.FullName.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.PhoneNumber.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.ProjectName.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return View(new SuperAdminJudgesViewModel(q, rows));
    }

    public async Task<IActionResult> Users(string? q, CancellationToken ct)
    {
        var users = await _userService.GetAllAsync(ct);
        if (!string.IsNullOrWhiteSpace(q))
        {
            users = users
                .Where(u => u.Username.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || u.FullName.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return View(new SuperAdminUsersViewModel(q, users));
    }

    [HttpGet]
    public IActionResult CreateUser() => View(new CreateUserViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(CreateUserViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _userService.CreateAsync(model.Username, model.Password, model.FullName, model.Role, ct);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        TempData["Success"] = "Foydalanuvchi qo'shildi.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> EditUser(int id, CancellationToken ct)
    {
        var user = await _userService.GetByIdAsync(id, ct);
        if (user is null)
        {
            return NotFound();
        }

        return View(new EditUserViewModel { Id = user.Id, Username = user.Username, FullName = user.FullName, Role = user.Role });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(EditUserViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _userService.EditUserAsync(model.Id, model.Username, model.FullName, model.NewPassword, ct);
            await _userService.ChangeRoleAsync(model.Id, model.Role, ct);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        TempData["Success"] = "Foydalanuvchi yangilandi.";
        return RedirectToAction(nameof(Users));
    }

    public async Task<IActionResult> Scores(string? q, CancellationToken ct)
    {
        // Mirrors SuperAdminPanelForm.LoadScoresAsync/ApplyScoresFilter exactly: a flat cross-project score
        // list, joined in-memory against presentations/judges/criteria fetched once each.
        var allScores = await _scoreService.GetAllAsync(ct);
        var presentationsById = (await _queueService.GetAllAsync(ct)).ToDictionary(p => p.Id);
        var judgesById = (await _judgeService.GetAllAsync(ct)).ToDictionary(j => j.Id);
        var criteriaById = (await _criterionService.GetAllAsync(ct)).ToDictionary(c => c.Id);

        var rows = allScores
            .Select(s => new SuperAdminScoreRow(
                presentationsById.TryGetValue(s.PresentationId, out var p) ? p.Title : "?",
                judgesById.TryGetValue(s.JudgeId, out var j) ? j.PhoneNumber : "?",
                criteriaById.TryGetValue(s.CriterionId, out var c) ? c.Name : "?",
                s.Value,
                s.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")))
            .ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            rows = rows
                .Where(r => r.PresentationTitle.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.JudgePhone.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.CriterionName.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return View(new SuperAdminScoresViewModel(q, rows));
    }

    public async Task<IActionResult> ExportFinalScores(int projectId, CancellationToken ct)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, ct);
        if (project is null)
        {
            return NotFound();
        }

        var criteria = await _criterionService.GetByProjectIdAsync(projectId, ct);
        var rows = await _scoreService.GetFinalScoresAsync(projectId, ct);
        var bytes = FinalScoresExcelExporter.Export(project.Name, criteria, rows);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "yakuniy-baholar.xlsx");
    }

    public async Task<IActionResult> Jurnal(string? q, CancellationToken ct)
    {
        var entries = await _historyRepository.GetRecentAsync(200, ct);
        if (!string.IsNullOrWhiteSpace(q))
        {
            entries = entries.Where(e => e.Message.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return View(new SuperAdminJurnalViewModel(q, entries));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearHistory(CancellationToken ct)
    {
        await _historyRepository.ClearAllAsync(ct);
        TempData["Success"] = "Jurnal tozalandi.";
        return RedirectToAction(nameof(Jurnal));
    }
}
