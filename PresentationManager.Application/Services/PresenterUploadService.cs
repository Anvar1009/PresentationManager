using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.Application.Services;

/// <summary>Backs the "open in browser" alternative to sending a file straight into the Telegram chat (see
/// <c>PresentationBotHostedService</c>'s AwaitingFile step) - a Telegram Mini App page that uploads over a
/// plain HTTPS POST instead of through the Telegram Bot API, whose own file downloads are capped at 20MB.
/// Used from both processes that never share memory: PresentationManager.BotService mints the token
/// (<see cref="CreateTokenAsync"/>), PresentationManager.API's PresenterController consumes it
/// (<see cref="GetValidTokenAsync"/>/<see cref="SubmitAsync"/>) - both only ever share the database.</summary>
public sealed class PresenterUploadService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    private readonly IPresenterUploadTokenRepository _tokenRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly PresentationQueueService _queueService;
    private readonly ILogger<PresenterUploadService> _logger;

    public PresenterUploadService(
        IPresenterUploadTokenRepository tokenRepository, IProjectRepository projectRepository,
        ISettingsRepository settingsRepository, PresentationQueueService queueService,
        ILogger<PresenterUploadService> logger)
    {
        _tokenRepository = tokenRepository;
        _projectRepository = projectRepository;
        _settingsRepository = settingsRepository;
        _queueService = queueService;
        _logger = logger;
    }

    /// <summary>Mints a fresh single-use link token for the upload the bot conversation just gathered project/
    /// title/existing-submission for - see <c>ChatSession</c>, whose fields this mirrors 1:1.</summary>
    public async Task<string> CreateTokenAsync(
        long chatId, int projectId, string projectName, int? presenterId, string fullName, string title,
        int? existingPresentationId, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        await _tokenRepository.AddAsync(new PresenterUploadToken
        {
            Token = token,
            ChatId = chatId,
            ProjectId = projectId,
            ProjectName = projectName,
            PresenterId = presenterId,
            FullName = fullName,
            Title = title,
            ExistingPresentationId = existingPresentationId,
            ExpiresAt = DateTime.UtcNow.Add(TokenLifetime)
        }, ct);
        return token;
    }

    /// <summary>Null for anything the upload page must treat as "this link no longer works" - unknown,
    /// already-used, or expired - without distinguishing which to the presenter (none of them are actionable
    /// beyond "go back to Telegram and tap the button again").</summary>
    public async Task<PresenterUploadToken?> GetValidTokenAsync(string token, CancellationToken ct = default)
    {
        var record = await _tokenRepository.GetByTokenAsync(token, ct);
        return record is null || record.UsedAt is not null || record.ExpiresAt < DateTime.UtcNow ? null : record;
    }

    /// <summary>Saves the uploaded file into the same managed storage/queue the in-chat flow uses
    /// (<see cref="PresentationQueueService.AddAsync"/>/<see cref="PresentationQueueService.UpdateAsync"/>),
    /// then burns the token so the link can't be replayed. Re-validates the token and the project's submission
    /// deadline itself rather than trusting the caller already did (defense-in-depth, same reasoning as
    /// <see cref="PresentationQueueService.AddAsync"/>'s own presenter-assignment re-check) - throws
    /// <see cref="InvalidOperationException"/> with a presenter-facing Uzbek message on any rejection.</summary>
    public async Task<PresenterUploadToken> SubmitAsync(
        string token, string sourceFilePath, PresentationFileType fileType, CancellationToken ct = default)
    {
        var record = await GetValidTokenAsync(token, ct)
            ?? throw new InvalidOperationException("Havola yaroqsiz yoki muddati o'tgan. Botga qaytib, qaytadan urinib ko'ring.");

        var project = await _projectRepository.GetByIdAsync(record.ProjectId, ct);
        if (project?.SubmissionDeadline is { } deadline && DateTime.UtcNow > deadline)
        {
            throw new InvalidOperationException(
                $"Taqdimot topshirish/yangilash muddati tugagan ({deadline.ToLocalTime():dd.MM.yyyy HH:mm}). Fayl qabul qilinmadi.");
        }

        var settings = await _settingsRepository.GetAsync(ct);
        if (record.ExistingPresentationId is { } existingId)
        {
            await _queueService.UpdateAsync(
                existingId, record.FullName, record.Title,
                settings.DefaultPresentationTimeSeconds, settings.DefaultDiscussionTimeSeconds, extraDiscussionTimeSeconds: 0,
                sourceFilePath, fileType, ct);
        }
        else
        {
            await _queueService.AddAsync(
                record.ProjectId, record.FullName, record.Title,
                sourceFilePath, fileType,
                settings.DefaultPresentationTimeSeconds, settings.DefaultDiscussionTimeSeconds,
                extraDiscussionTimeSeconds: 0, presenterId: record.PresenterId, ct: ct);
        }

        await _tokenRepository.MarkUsedAsync(record.Id, ct);
        _logger.LogInformation(
            "Taqdimot mini-app orqali qabul qilindi: chat {ChatId}, loyiha {ProjectId}, sarlavha \"{Title}\"",
            record.ChatId, record.ProjectId, record.Title);
        return record;
    }
}
