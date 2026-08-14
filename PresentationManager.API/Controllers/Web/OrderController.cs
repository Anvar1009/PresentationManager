using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PresentationManager.API.Hubs;
using PresentationManager.API.Models;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Controllers.Web;

/// <summary>"Tartib operatori" role's one screen - pick a project, shuffle its presentation order. Unlike
/// <see cref="JudgeController"/>, this role isn't scoped to specific project assignments (see
/// UserRole.OrderOperator's own doc comment), so every project is offered. The shuffle itself reuses
/// <see cref="PresentationQueueService.RandomizeOrderAsync"/> and broadcasts over the same
/// <see cref="PresentationOrderHub"/> AdminForm's desktop queue view already listens to - this used to be a
/// JWT-authenticated REST endpoint called from a WinForms form; both moved here once the role went web-only.</summary>
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
        var options = projects.Select(p => new OrderProjectOption(p.Id, p.Name)).ToList();
        return View(new OrderDashboardViewModel(User.Identity?.Name ?? string.Empty, options));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Randomize(int projectId, CancellationToken ct)
    {
        await _queueService.RandomizeOrderAsync(projectId, ct);
        await _orderHub.Clients.All.SendAsync("OrderRandomized", projectId, ct);

        TempData["Success"] = "Tartib tasodifiy belgilandi.";
        return RedirectToAction(nameof(Dashboard));
    }
}
