using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.API.Controllers;

/// <summary>Thin HTTP mirror of <see cref="IPresentationRepository"/> - see ProjectsController's remarks
/// on why this has no business logic of its own (that stays client-side in
/// PresentationManager.Application.Services.PresentationQueueService/PresentationSessionController). The
/// "Tartib operatori" randomize-order action used to live here too, but moved to the web platform's
/// Controllers\Web\OrderController once that role became web-only - see UserRole.OrderOperator's own doc
/// comment.</summary>
[ApiController]
[Authorize]
[Route("api/presentations")]
public sealed class PresentationsController : ControllerBase
{
    private readonly IPresentationRepository _presentationRepository;
    private readonly ILogger<PresentationsController> _logger;

    public PresentationsController(IPresentationRepository presentationRepository, ILogger<PresentationsController> logger)
    {
        _presentationRepository = presentationRepository;
        _logger = logger;
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
        _logger.LogInformation("Taqdimot API orqali qo'shildi: {PresentationId} - {Title}", created.Id, created.Title);
        return Ok(PresentationDto.FromEntity(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, PresentationDto request, CancellationToken ct)
    {
        var entity = request.ToEntity();
        entity.Id = id;
        await _presentationRepository.UpdateAsync(entity, ct);
        _logger.LogInformation("Taqdimot API orqali yangilandi: {PresentationId}", id);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _presentationRepository.DeleteAsync(id, ct);
        _logger.LogInformation("Taqdimot API orqali o'chirildi: {PresentationId}", id);
        return NoContent();
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder(ReorderPresentationsRequest request, CancellationToken ct)
    {
        await _presentationRepository.ReorderAsync(request.OrderedPresentationIds, ct);
        _logger.LogInformation("Navbat tartibi API orqali o'zgartirildi: {Count} ta taqdimot", request.OrderedPresentationIds.Count);
        return NoContent();
    }
}
