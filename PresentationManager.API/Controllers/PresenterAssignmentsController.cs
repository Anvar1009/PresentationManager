using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.TelegramBot;

namespace PresentationManager.API.Controllers;

/// <summary>Admin panel's "loyihaga biriktirish" (approve a bot-registered presenter for a project) endpoint.
/// <see cref="Add"/> both creates the row (via <see cref="PresenterAssignmentService.AssignAsync"/>, which
/// also rejects a duplicate assignment) and pushes the presenter a Telegram notification the moment it
/// succeeds - same "controller does the server-side push" pattern as
/// <see cref="JudgesController"/>.Add, needed for the same reason: the service instance that creates the row
/// runs client-side in PresentationManager.UI (over HTTP), so this controller is the only place server-side
/// that ever sees an assignment get added, regardless of which desktop triggered it.</summary>
[ApiController]
[Authorize]
[Route("api/presenter-assignments")]
public sealed class PresenterAssignmentsController : ControllerBase
{
    private readonly IPresenterProjectAssignmentRepository _assignmentRepository;
    private readonly PresenterAssignmentService _assignmentService;
    private readonly IPresenterRepository _presenterRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly ILogger<PresenterAssignmentsController> _logger;

    public PresenterAssignmentsController(
        IPresenterProjectAssignmentRepository assignmentRepository,
        PresenterAssignmentService assignmentService,
        IPresenterRepository presenterRepository,
        IProjectRepository projectRepository,
        TelegramNotifier telegramNotifier,
        ILogger<PresenterAssignmentsController> logger)
    {
        _assignmentRepository = assignmentRepository;
        _assignmentService = assignmentService;
        _presenterRepository = presenterRepository;
        _projectRepository = projectRepository;
        _telegramNotifier = telegramNotifier;
        _logger = logger;
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<ActionResult<List<PresenterProjectAssignmentDto>>> GetByProject(int projectId, CancellationToken ct)
    {
        var assignments = await _assignmentRepository.GetByProjectIdAsync(projectId, ct);
        return Ok(assignments.Select(PresenterProjectAssignmentDto.FromEntity).ToList());
    }

    [HttpGet("presenter/{presenterId:int}")]
    public async Task<ActionResult<List<PresenterProjectAssignmentDto>>> GetByPresenter(int presenterId, CancellationToken ct)
    {
        var assignments = await _assignmentRepository.GetByPresenterIdAsync(presenterId, ct);
        return Ok(assignments.Select(PresenterProjectAssignmentDto.FromEntity).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<PresenterProjectAssignmentDto>> Add(PresenterProjectAssignmentDto request, CancellationToken ct)
    {
        try
        {
            var created = await _assignmentService.AssignAsync(request.ProjectId, request.PresenterId, ct);
            _logger.LogInformation("Taqdimotchi loyihaga biriktirildi: presenter {PresenterId} -> loyiha {ProjectId}",
                request.PresenterId, request.ProjectId);

            var presenter = await _presenterRepository.GetByIdAsync(created.PresenterId, ct);
            if (presenter?.TelegramChatId is { } chatId)
            {
                // Best-effort, same as JudgesController.Add - the assignment itself already succeeded
                // regardless of whether this push reaches them (e.g. they blocked the bot). Includes the
                // event date/time/location (EventReminderFormatter - same text the bot itself appends after
                // a successful upload) so the presenter knows when to actually show up, not just that
                // they're approved; replyMarkup docks PresentationBotHostedService.PresenterMainKeyboard
                // immediately, so "Taqdimot jo'natish" is there even before their next /start.
                var project = await _projectRepository.GetByIdAsync(created.ProjectId, ct);
                var projectName = project?.Name ?? "loyiha";
                var message = $"🎉 Tabriklaymiz! Siz \"{projectName}\" loyihasida ishtirok etish uchun tasdiqlandingiz.\nEndi taqdimotingizni yuborishingiz mumkin - buning uchun pastdagi \"📤 Taqdimot jo'natish\" tugmasini bosing.";
                if (project is not null)
                {
                    message += $"\n\n{EventReminderFormatter.Format(project)}";
                }

                await _telegramNotifier.TrySendMessageAsync(chatId, message, PresentationBotHostedService.PresenterMainKeyboard, ct);
            }

            return Ok(PresenterProjectAssignmentDto.FromEntity(created));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Taqdimotchini loyihaga biriktirish rad etildi: {Reason}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _assignmentService.DeleteAsync(id, ct);
        _logger.LogInformation("Taqdimotchi biriktiruvi o'chirildi: {AssignmentId}", id);
        return NoContent();
    }
}
