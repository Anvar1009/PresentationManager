using System.Diagnostics;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;

namespace PresentationManager.TelegramBot;

/// <summary>Subscribes to <c>JudgeService.JudgeAssigned</c> and pushes the newly-assigned judge a Telegram
/// notification immediately, instead of leaving them to discover it only if/when they happen to press /start
/// again on their own. Registered (and eagerly resolved) in every process that might call
/// <c>JudgeService.AssignAsync</c> - both the desktop app (Admin assigning via <c>JudgeManagementForm</c>) and
/// the bot's own worker process (a linked Admin assigning via the Telegram-side admin mirror) - since
/// <see cref="JudgeService"/> is a plain per-process singleton and this C# event never crosses a process
/// boundary on its own; each process's own copy only ever sees assignments made within that same process.</summary>
public sealed class JudgeAssignmentNotifier
{
    private readonly ProjectService _projectService;
    private readonly TelegramNotifier _telegramNotifier;

    public JudgeAssignmentNotifier(JudgeService judgeService, ProjectService projectService, TelegramNotifier telegramNotifier)
    {
        _projectService = projectService;
        _telegramNotifier = telegramNotifier;

        judgeService.JudgeAssigned += judge => _ = OnJudgeAssignedAsync(judge);
    }

    private async Task OnJudgeAssignedAsync(Judge judge)
    {
        if (judge.TelegramChatId is not { } chatId)
        {
            return;
        }

        try
        {
            var projects = await _projectService.GetAllAsync();
            var projectName = projects.FirstOrDefault(p => p.Id == judge.ProjectId)?.Name ?? "loyiha";

            await _telegramNotifier.TrySendMessageAsync(chatId,
                $"🎉 Tabriklaymiz! Siz \"{projectName}\" loyihasi uchun hakam etib tayinlandingiz.\nPastdagi \"📋 Loyihalar\" tugmasi orqali istalgan vaqt boshlashingiz mumkin.",
                PresentationBotHostedService.JudgeMainKeyboard);
        }
        catch (Exception ex)
        {
            // Best-effort push notification - the assignment itself already succeeded regardless of whether
            // this message manages to reach them (e.g. they blocked the bot).
            Debug.WriteLine($"Failed to notify newly-assigned judge (chat {chatId}): {ex}");
        }
    }
}
