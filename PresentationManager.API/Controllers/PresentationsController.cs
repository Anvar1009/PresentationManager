using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PresentationManager.API.Dtos;
using PresentationManager.API.Hubs;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Controllers;

/// <summary>Thin HTTP mirror of <see cref="IPresentationRepository"/> - see ProjectsController's remarks
/// on why this has no business logic of its own (that stays client-side in
/// PresentationManager.Application.Services.PresentationQueueService/PresentationSessionController). The one
/// exception is <see cref="RandomizeOrder"/>, whose shuffle logic lives in
/// <see cref="PresentationQueueService.RandomizeOrderAsync"/> - this action itself only authorizes the call
/// and relays the resulting change to connected clients over <see cref="PresentationOrderHub"/>.</summary>
[ApiController]
[Authorize]
[Route("api/presentations")]
public sealed class PresentationsController : ControllerBase
{
    private readonly IPresentationRepository _presentationRepository;
    private readonly PresentationQueueService _queueService;
    private readonly IHubContext<PresentationOrderHub> _orderHub;

    public PresentationsController(
        IPresentationRepository presentationRepository, PresentationQueueService queueService, IHubContext<PresentationOrderHub> orderHub)
    {
        _presentationRepository = presentationRepository;
        _queueService = queueService;
        _orderHub = orderHub;
    }

    [HttpGet]
    public async Task<ActionResult<List<PresentationDto>>> GetAll(CancellationToken ct)
    {
        var presentations = await _presentationRepository.GetAllAsync(ct);
        return Ok(presentations.Select(PresentationDto.FromEntity).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PresentationDto>> GetById(int id, CancellationToken ct)
    {
        var presentation = await _presentationRepository.GetByIdAsync(id, ct);
        return presentation is null ? NotFound() : Ok(PresentationDto.FromEntity(presentation));
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<ActionResult<List<PresentationDto>>> GetByProject(int projectId, CancellationToken ct)
    {
        var presentations = await _presentationRepository.GetByProjectIdAsync(projectId, ct);
        return Ok(presentations.Select(PresentationDto.FromEntity).ToList());
    }

    [HttpGet("project/{projectId:int}/ordered")]
    public async Task<ActionResult<List<PresentationDto>>> GetOrderedByProject(int projectId, CancellationToken ct)
    {
        var presentations = await _presentationRepository.GetAllOrderedAsync(projectId, ct);
        return Ok(presentations.Select(PresentationDto.FromEntity).ToList());
    }

    [HttpGet("project/{projectId:int}/search")]
    public async Task<ActionResult<List<PresentationDto>>> Search(int projectId, [FromQuery] string q, CancellationToken ct)
    {
        var presentations = await _presentationRepository.SearchByNameAsync(q, projectId, ct);
        return Ok(presentations.Select(PresentationDto.FromEntity).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<PresentationDto>> Add(PresentationDto request, CancellationToken ct)
    {
        var created = await _presentationRepository.AddAsync(request.ToEntity(), ct);
        return Ok(PresentationDto.FromEntity(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, PresentationDto request, CancellationToken ct)
    {
        var entity = request.ToEntity();
        entity.Id = id;
        await _presentationRepository.UpdateAsync(entity, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _presentationRepository.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder(ReorderPresentationsRequest request, CancellationToken ct)
    {
        await _presentationRepository.ReorderAsync(request.OrderedPresentationIds, ct);
        return NoContent();
    }

    /// <summary>"Tartib operatori" role's one write action - randomly shuffles a project's presentation
    /// order and pushes an "OrderRandomized" event (payload: <paramref name="projectId"/>) to every
    /// connected <see cref="PresentationOrderHub"/> client; a client not currently viewing this project just
    /// ignores it (see AdminForm's handler).</summary>
    [HttpPost("project/{projectId:int}/randomize-order")]
    [Authorize(Roles = nameof(UserRole.OrderOperator))]
    public async Task<IActionResult> RandomizeOrder(int projectId, CancellationToken ct)
    {
        await _queueService.RandomizeOrderAsync(projectId, ct);
        await _orderHub.Clients.All.SendAsync("OrderRandomized", projectId, ct);
        return NoContent();
    }
}
