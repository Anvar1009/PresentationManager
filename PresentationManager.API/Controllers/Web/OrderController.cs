using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PresentationManager.API.Hubs;
using PresentationManager.API.Models;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Controllers.Web;

/// <summary>"Tartib operatori" role's flow - pick a project (<see cref="Dashboard"/>), see that project's
/// presenters on their own page (<see cref="Project"/>), then generate the random order
/// (<see cref="Randomize"/>). Unlike <see cref="JudgeController"/>, this role isn't scoped to specific
/// project assignments (see UserRole.OrderOperator's own doc comment), so every project is offered. The
/// shuffle itself reuses <see cref="PresentationQueueService.RandomizeOrderAsync"/> and broadcasts over the
/// same <see cref="PresentationOrderHub"/> AdminForm's desktop queue view already listens to.</summary>
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Roles = nameof(UserRole.OrderOperator))]
public sealed class OrderController : Controller
{
    private readonly ProjectService _projectService;
    private readonly PresentationQueueService _queueService;
    private readonly IHubContext<PresentationOrderHub> _orderHub;

    public OrderController(ProjectService projectService, PresentationQueueService queueService, IHubContext<PresentationOrderHub> orderHub)
    {
        _projectService = projectService;
        _queueService = queueService;
        _orderHub = orderHub;
    }

    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var projects = await _projectService.GetAllAsync(ct);

        // One count query per project rather than a single bulk fetch - projects.Count stays small in
        // practice (this is a per-event competition list, not a paginated table), and CountPresentationsAsync
        // already exists for AdminController's delete-warning, so this reuses it instead of adding a new
        // repository method just for this card.
        var options = new List<OrderProjectOption>();
        foreach (var project in projects)
        {
            var count = await _projectService.CountPresentationsAsync(project.Id, ct);
            options.Add(new OrderProjectOption(project.Id, project.Name, count));
        }

        return View(new OrderDashboardViewModel(User.Identity?.Name ?? string.Empty, options));
    }

    /// <summary>The project a "Tartib operatori" actually works from - just that project's presenters, in
    /// their current order, with the one action (<see cref="Randomize"/>) at the bottom. Re-rendering this
    /// same page after a randomize (rather than bouncing back to <see cref="Dashboard"/>) is what shows the
    /// operator the new order took effect.</summary>
    public async Task<IActionResult> Project(int projectId, CancellationToken ct)
    {
        var projects = await _projectService.GetAllAsync(ct);
        var project = projects.FirstOrDefault(p => p.Id == projectId);
        if (project is null)
        {
            return NotFound();
        }

        var presentations = await _queueService.GetAllAsync(projectId, ct);
        var names = presentations.Select(p => p.FullName).ToList();

        return View(new OrderProjectViewModel(project.Id, project.Name, names));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Randomize(int projectId, CancellationToken ct)
    {
        await _queueService.RandomizeOrderAsync(projectId, ct);
        await _orderHub.Clients.All.SendAsync("OrderRandomized", projectId, ct);

        TempData["Success"] = "Ro'yxat shakllantirildi.";
        return RedirectToAction(nameof(Project), new { projectId });
    }
}
