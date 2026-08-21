using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.API.Controllers;

/// <summary>Thin HTTP mirror of <see cref="ICriterionRepository"/>.</summary>
[ApiController]
[Authorize]
[Route("api/criteria")]
public sealed class CriteriaController : ControllerBase
{
    private readonly ICriterionRepository _criterionRepository;
    private readonly ILogger<CriteriaController> _logger;

    public CriteriaController(ICriterionRepository criterionRepository, ILogger<CriteriaController> logger)
    {
        _criterionRepository = criterionRepository;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<CriterionDto>>> GetAll(CancellationToken ct)
    {
        var criteria = await _criterionRepository.GetAllAsync(ct);
        return Ok(criteria.Select(CriterionDto.FromEntity).ToList());
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<ActionResult<List<CriterionDto>>> GetByProject(int projectId, CancellationToken ct)
    {
        var criteria = await _criterionRepository.GetByProjectIdAsync(projectId, ct);
        return Ok(criteria.Select(CriterionDto.FromEntity).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<CriterionDto>> Add(CriterionDto request, CancellationToken ct)
    {
        var created = await _criterionRepository.AddAsync(request.ToEntity(), ct);
        _logger.LogInformation("Baholash mezoni qo'shildi: {CriterionId} - {Name}", created.Id, created.Name);
        return Ok(CriterionDto.FromEntity(created));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _criterionRepository.DeleteAsync(id, ct);
        _logger.LogInformation("Baholash mezoni o'chirildi: {CriterionId}", id);
        return NoContent();
    }
}
