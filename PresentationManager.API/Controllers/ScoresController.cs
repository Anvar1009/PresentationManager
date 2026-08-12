using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.API.Controllers;

/// <summary>Thin HTTP mirror of <see cref="IScoreRepository"/>.</summary>
[ApiController]
[Authorize]
[Route("api/scores")]
public sealed class ScoresController : ControllerBase
{
    private readonly IScoreRepository _scoreRepository;

    public ScoresController(IScoreRepository scoreRepository)
    {
        _scoreRepository = scoreRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<ScoreDto>>> GetAll(CancellationToken ct)
    {
        var scores = await _scoreRepository.GetAllAsync(ct);
        return Ok(scores.Select(ScoreDto.FromEntity).ToList());
    }

    [HttpGet("presentation/{presentationId:int}/judge/{judgeId:int}")]
    public async Task<ActionResult<List<ScoreDto>>> GetByPresentationAndJudge(int presentationId, int judgeId, CancellationToken ct)
    {
        var scores = await _scoreRepository.GetByPresentationAndJudgeAsync(presentationId, judgeId, ct);
        return Ok(scores.Select(ScoreDto.FromEntity).ToList());
    }

    /// <summary>Comma-separated presentation ids, e.g. <c>?ids=1,2,3</c> - see
    /// <see cref="IScoreRepository.GetByPresentationIdsAsync"/>.</summary>
    [HttpGet("by-presentations")]
    public async Task<ActionResult<List<ScoreDto>>> GetByPresentationIds([FromQuery] string ids, CancellationToken ct)
    {
        var presentationIds = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToList();
        var scores = await _scoreRepository.GetByPresentationIdsAsync(presentationIds, ct);
        return Ok(scores.Select(ScoreDto.FromEntity).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Upsert(UpsertScoreRequest request, CancellationToken ct)
    {
        await _scoreRepository.UpsertAsync(request.PresentationId, request.JudgeId, request.CriterionId, request.Value, ct);
        return NoContent();
    }
}
