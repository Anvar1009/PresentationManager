using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Controllers;

/// <summary>Thin HTTP mirror of <see cref="IHistoryRepository"/>.</summary>
[ApiController]
[Authorize]
[Route("api/history")]
public sealed class HistoryController : ControllerBase
{
    private readonly IHistoryRepository _historyRepository;

    public HistoryController(IHistoryRepository historyRepository)
    {
        _historyRepository = historyRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<HistoryEntryDto>>> GetRecent([FromQuery] int count, CancellationToken ct)
    {
        var entries = await _historyRepository.GetRecentAsync(count is > 0 ? count : 200, ct);
        return Ok(entries.Select(HistoryEntryDto.FromEntity).ToList());
    }

    [HttpGet("presentation/{presentationId:int}")]
    public async Task<ActionResult<List<HistoryEntryDto>>> GetForPresentation(int presentationId, CancellationToken ct)
    {
        var entries = await _historyRepository.GetForPresentationAsync(presentationId, ct);
        return Ok(entries.Select(HistoryEntryDto.FromEntity).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Add(CreateHistoryEntryRequest request, CancellationToken ct)
    {
        await _historyRepository.AddAsync(new HistoryEntry
        {
            PresentationId = request.PresentationId,
            EventType = request.EventType,
            Message = request.Message,
            Timestamp = request.Timestamp
        }, ct);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> ClearAll(CancellationToken ct)
    {
        await _historyRepository.ClearAllAsync(ct);
        return NoContent();
    }
}
