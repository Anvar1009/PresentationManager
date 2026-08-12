using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;
using PresentationManager.TelegramBot;

namespace PresentationManager.API.Controllers;

/// <summary>Thin HTTP mirror of <see cref="IJudgeRepository"/> - except Add, which also pushes the newly
/// assigned judge a Telegram notification, the same message/keyboard
/// PresentationManager.TelegramBot.JudgeAssignmentNotifier sends for BotService's own linked-Admin-mirror
/// assignment flow. That class can't be reused as-is here: it subscribes to an in-process C# event on
/// PresentationManager.Application.Services.JudgeService, but the JudgeService instance that actually
/// creates the row now runs client-side in PresentationManager.UI (via IJudgeRepository over HTTP) - this
/// controller is the only place server-side that ever sees a judge get added, regardless of which desktop
/// triggered it.</summary>
[ApiController]
[Authorize]
[Route("api/judges")]
public sealed class JudgesController : ControllerBase
{
    private readonly IJudgeRepository _judgeRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly TelegramNotifier _telegramNotifier;

    public JudgesController(IJudgeRepository judgeRepository, IProjectRepository projectRepository, TelegramNotifier telegramNotifier)
    {
        _judgeRepository = judgeRepository;
        _projectRepository = projectRepository;
        _telegramNotifier = telegramNotifier;
    }

    [HttpGet]
    public async Task<ActionResult<List<JudgeDto>>> GetAll(CancellationToken ct)
    {
        var judges = await _judgeRepository.GetAllAsync(ct);
        return Ok(judges.Select(JudgeDto.FromEntity).ToList());
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<ActionResult<List<JudgeDto>>> GetByProject(int projectId, CancellationToken ct)
    {
        var judges = await _judgeRepository.GetByProjectIdAsync(projectId, ct);
        return Ok(judges.Select(JudgeDto.FromEntity).ToList());
    }

    [HttpGet("by-telegram/{chatId:long}")]
    public async Task<ActionResult<List<JudgeDto>>> GetByTelegramChatId(long chatId, CancellationToken ct)
    {
        var judges = await _judgeRepository.GetByTelegramChatIdAsync(chatId, ct);
        return Ok(judges.Select(JudgeDto.FromEntity).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<JudgeDto>> Add(JudgeDto request, CancellationToken ct)
    {
        var created = await _judgeRepository.AddAsync(request.ToEntity(), ct);

        if (created.TelegramChatId is { } chatId)
        {
            // Best-effort, same as JudgeAssignmentNotifier - the assignment itself already succeeded
            // regardless of whether this push reaches them (e.g. they blocked the bot).
            var project = await _projectRepository.GetByIdAsync(created.ProjectId, ct);
            var projectName = project?.Name ?? "loyiha";
            await _telegramNotifier.TrySendMessageAsync(chatId,
                $"🎉 Tabriklaymiz! Siz \"{projectName}\" loyihasi uchun hakam etib tayinlandingiz.\nPastdagi \"📋 Loyihalar\" tugmasi orqali istalgan vaqt boshlashingiz mumkin.",
                PresentationBotHostedService.JudgeMainKeyboard, ct);
        }

        return Ok(JudgeDto.FromEntity(created));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _judgeRepository.DeleteAsync(id, ct);
        return NoContent();
    }
}
