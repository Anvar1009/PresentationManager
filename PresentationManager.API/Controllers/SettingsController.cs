using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.API.Controllers;

/// <summary>Thin HTTP mirror of <see cref="ISettingsRepository"/> - the single-row app settings table.</summary>
[ApiController]
[Authorize]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly ISettingsRepository _settingsRepository;

    public SettingsController(ISettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
    }

    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken ct) =>
        Ok(SettingsDto.FromEntity(await _settingsRepository.GetAsync(ct)));

    [HttpPut]
    public async Task<IActionResult> Save(SettingsDto request, CancellationToken ct)
    {
        await _settingsRepository.SaveAsync(request.ToEntity(), ct);
        return NoContent();
    }
}
