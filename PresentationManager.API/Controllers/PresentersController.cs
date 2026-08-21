using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.API.Controllers;

/// <summary>Thin HTTP mirror of <see cref="IPresenterRepository"/>.</summary>
[ApiController]
[Authorize]
[Route("api/presenters")]
public sealed class PresentersController : ControllerBase
{
    private readonly IPresenterRepository _presenterRepository;
    private readonly ILogger<PresentersController> _logger;

    public PresentersController(IPresenterRepository presenterRepository, ILogger<PresentersController> logger)
    {
        _presenterRepository = presenterRepository;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<PresenterDto>>> GetAll(CancellationToken ct)
    {
        var presenters = await _presenterRepository.GetAllAsync(ct);
        return Ok(presenters.Select(PresenterDto.FromEntity).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PresenterDto>> GetById(int id, CancellationToken ct)
    {
        var presenter = await _presenterRepository.GetByIdAsync(id, ct);
        return presenter is null ? NotFound() : Ok(PresenterDto.FromEntity(presenter));
    }

    [HttpGet("by-telegram/{chatId:long}")]
    public async Task<ActionResult<PresenterDto>> GetByTelegramChatId(long chatId, CancellationToken ct)
    {
        var presenter = await _presenterRepository.GetByTelegramChatIdAsync(chatId, ct);
        return presenter is null ? NotFound() : Ok(PresenterDto.FromEntity(presenter));
    }

    [HttpPost]
    public async Task<ActionResult<PresenterDto>> Add(PresenterDto request, CancellationToken ct)
    {
        var created = await _presenterRepository.AddAsync(request.ToEntity(), ct);
        _logger.LogInformation("Taqdimotchi qo'shildi: {PresenterId} - {FullName}", created.Id, created.FullName);
        return Ok(PresenterDto.FromEntity(created));
    }
}
