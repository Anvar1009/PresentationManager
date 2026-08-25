using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationManager.API.Models;
using PresentationManager.API.Services;
using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Controllers.Web;

/// <summary>Web mirror of <see cref="SuperAdminController"/>, scoped to one <see cref="Domain.Entities.Organization"/>
/// instead of the whole system - every list/stat here is filtered to <see cref="CurrentOrganizationId"/> using
/// the org-scoped repository methods added alongside <see cref="Domain.Entities.Project.OrganizationId"/>
/// (<c>GetByOrganizationAsync</c> on Project/Presentation/Judge/Score/History/PresenterProjectAssignment/User).
/// The one deliberate narrowing versus SuperAdmin: <see cref="CreateUser(ManagerCreateUserViewModel, CancellationToken)"/>
/// only ever creates <see cref="UserRole.Admin"/>/<see cref="UserRole.Operator"/> accounts, always stamped with
/// this Manager's own organization - never another Manager or a SuperAdmin.</summary>
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Roles = nameof(UserRole.Manager))]
public sealed class ManagerController : Controller
{
    private readonly ProjectService _projectService;
    private readonly PresentationQueueService _queueService;
    private readonly UserService _userService;
    private readonly JudgeService _judgeService;
    private readonly CriterionService _criterionService;
    private readonly ScoreService _scoreService;
    private readonly PresenterAssignmentService _assignmentService;
    private readonly IPresenterRepository _presenterRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IPresentationRepository _presentationRepository;
    private readonly IHistoryRepository _historyRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly OrganizationService _organizationService;
    private readonly ILogger<ManagerController> _logger;

    public ManagerController(
        ProjectService projectService, PresentationQueueService queueService, UserService userService,
        JudgeService judgeService, CriterionService criterionService, ScoreService scoreService,
        PresenterAssignmentService assignmentService, IPresenterRepository presenterRepository,
        IProjectRepository projectRepository, IPresentationRepository presentationRepository,
        IHistoryRepository historyRepository, IFileStorageService fileStorageService,
        OrganizationService organizationService, ILogger<ManagerController> logger)
    {
        _projectService = projectService;
        _queueService = queueService;
        _userService = userService;
        _judgeService = judgeService;
        _criterionService = criterionService;
        _scoreService = scoreService;
        _assignmentService = assignmentService;
        _presenterRepository = presenterRepository;
        _projectRepository = projectRepository;
        _presentationRepository = presentationRepository;
        _historyRepository = historyRepository;
        _fileStorageService = fileStorageService;
        _organizationService = organizationService;
        _logger = logger;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>Every Manager account is created with an organization already fixed (see
    /// <c>SuperAdminController.CreateManager</c>) - a missing claim here means the account predates
    /// organizations entirely, which every action below treats as "nothing to show" rather than throwing.</summary>
    private int? CurrentOrganizationId => User.FindFirst("OrganizationId") is { } claim ? int.Parse(claim.Value) : null;

    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        var organization = await _organizationService.GetByIdAsync(organizationId, ct);
        var projects = await _projectService.GetByOrganizationAsync(organizationId, ct);
        var users = await _userService.GetByOrganizationAsync(organizationId, ct);
        var judges = await _judgeService.GetByOrganizationAsync(organizationId, ct);
        var presentations = await _queueService.GetByOrganizationAsync(organizationId, ct);
        var presenterCount = (await _assignmentService.GetByOrganizationAsync(organizationId, ct))
            .Select(a => a.PresenterId).Distinct().Count();

        var byStatus = presentations
            .GroupBy(p => p.Status)
            .Select(g => new SuperAdminStatusCount(
                UzbekText.StatusLabel(g.Key), g.Count(),
                presentations.Count == 0 ? 0 : g.Count() * 100.0 / presentations.Count))
            .OrderByDescending(s => s.Count)
            .ToList();

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

        var recentActivity = (await _historyRepository.GetRecentByOrganizationAsync(organizationId, 5, ct))
            .Select(entry => new SuperAdminActivityRow(entry.Message, entry.EventType, FormatRecentTimestamp(entry.Timestamp)))
            .ToList();

        return View(new ManagerDashboardViewModel(
            User.Identity?.Name ?? string.Empty, organization?.Name ?? "?",
            projects.Count, presenterCount, users.Count, judges.Count, presentations.Count,
            byStatus, topProjects, recentActivity));
    }

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
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        var projects = await _projectService.GetByOrganizationAsync(organizationId, ct);
        if (!string.IsNullOrWhiteSpace(q))
        {
            projects = projects.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return View(new ManagerProjectsViewModel(q, projects));
    }

    public async Task<IActionResult> Presentations(string? q, CancellationToken ct)
    {
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        var presentations = await _queueService.GetByOrganizationAsync(organizationId, ct);
        var projectNames = (await _projectService.GetByOrganizationAsync(organizationId, ct)).ToDictionary(p => p.Id, p => p.Name);

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

    public async Task<IActionResult> DownloadPresentation(int presentationId, CancellationToken ct)
    {
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        var presentation = await _presentationRepository.GetByIdAsync(presentationId, ct);
        if (presentation is null)
        {
            return NotFound();
        }

        var project = await _projectRepository.GetByIdAsync(presentation.ProjectId, ct);
        if (project is null || (project.OrganizationId is int projectOrgId && projectOrgId != organizationId))
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
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        var assignments = await _assignmentService.GetByOrganizationAsync(organizationId, ct);
        var presenterIds = assignments.Select(a => a.PresenterId).ToHashSet();
        var presenters = (await _presenterRepository.GetAllAsync(ct)).Where(p => presenterIds.Contains(p.Id)).ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            presenters = presenters
                .Where(p => p.FullName.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (p.PhoneNumber?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return View(new ManagerPresentersViewModel(q, presenters));
    }

    public async Task<IActionResult> Judges(string? q, CancellationToken ct)
    {
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        var judges = await _judgeService.GetByOrganizationAsync(organizationId, ct);
        var projectNames = (await _projectService.GetByOrganizationAsync(organizationId, ct)).ToDictionary(p => p.Id, p => p.Name);

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
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        var users = (await _userService.GetByOrganizationAsync(organizationId, ct))
            .Where(u => u.Role is UserRole.Admin or UserRole.Operator)
            .ToList();
        if (!string.IsNullOrWhiteSpace(q))
        {
            users = users
                .Where(u => u.Username.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || u.FullName.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return View(new ManagerUsersViewModel(q, users));
    }

    [HttpGet]
    public IActionResult CreateUser() => View(new ManagerCreateUserViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(ManagerCreateUserViewModel model, CancellationToken ct)
    {
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        if (model.Role is not (UserRole.Admin or UserRole.Operator))
        {
            ModelState.AddModelError(string.Empty, "Faqat Admin yoki Operator rolini tanlash mumkin.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _userService.CreateAsync(model.Username, model.Password, model.FullName, model.Role, organizationId, ct);
            _logger.LogInformation("Menejer foydalanuvchi yaratdi: {Username} ({Role}, tashkilot {OrganizationId})",
                model.Username, model.Role, organizationId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Foydalanuvchi yaratish rad etildi: {Reason}", ex.Message);
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        TempData["Success"] = "Foydalanuvchi qo'shildi.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> EditUser(int id, CancellationToken ct)
    {
        if (await FindOwnUserAsync(id, ct) is not { } user)
        {
            return NotFound();
        }

        return View(new ManagerEditUserViewModel { Id = user.Id, Username = user.Username, FullName = user.FullName, Role = user.Role });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(ManagerEditUserViewModel model, CancellationToken ct)
    {
        if (await FindOwnUserAsync(model.Id, ct) is null)
        {
            return NotFound();
        }

        if (model.Role is not (UserRole.Admin or UserRole.Operator))
        {
            ModelState.AddModelError(string.Empty, "Faqat Admin yoki Operator rolini tanlash mumkin.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _userService.EditUserAsync(model.Id, model.Username, model.FullName, model.NewPassword, ct);
            await _userService.ChangeRoleAsync(model.Id, model.Role, ct);
            _logger.LogInformation("Menejer foydalanuvchini yangiladi: {UserId}", model.Id);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Foydalanuvchini yangilash rad etildi: {UserId}, {Reason}", model.Id, ex.Message);
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        TempData["Success"] = "Foydalanuvchi yangilandi.";
        return RedirectToAction(nameof(Users));
    }

    /// <summary>Confirms <paramref name="userId"/> is an Admin/Operator belonging to this Manager's own
    /// organization before any read/write against it - same defensive scoping
    /// <c>AdminController.FindOwnedProjectAsync</c> applies to projects, so one Manager can never reach
    /// another organization's account by guessing an id in the URL.</summary>
    private async Task<User?> FindOwnUserAsync(int userId, CancellationToken ct)
    {
        if (CurrentOrganizationId is not int organizationId)
        {
            return null;
        }

        var user = await _userService.GetByIdAsync(userId, ct);
        return user is not null && user.OrganizationId == organizationId && user.Role is UserRole.Admin or UserRole.Operator
            ? user
            : null;
    }

    public async Task<IActionResult> Scores(string? q, CancellationToken ct)
    {
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        var scores = await _scoreService.GetByOrganizationAsync(organizationId, ct);
        var presentationsById = (await _queueService.GetByOrganizationAsync(organizationId, ct)).ToDictionary(p => p.Id);
        var judgesById = (await _judgeService.GetByOrganizationAsync(organizationId, ct)).ToDictionary(j => j.Id);
        var criteriaById = (await _criterionService.GetAllAsync(ct)).ToDictionary(c => c.Id);

        var rows = scores
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
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        var project = await _projectRepository.GetByIdAsync(projectId, ct);
        if (project is null || (project.OrganizationId is int projectOrgId && projectOrgId != organizationId))
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
        if (CurrentOrganizationId is not int organizationId)
        {
            return Forbid();
        }

        var entries = await _historyRepository.GetRecentByOrganizationAsync(organizationId, 200, ct);
        if (!string.IsNullOrWhiteSpace(q))
        {
            entries = entries.Where(e => e.Message.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return View(new SuperAdminJurnalViewModel(q, entries));
    }
}
