using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.Application.Services;

/// <summary>One project this presenter is approved for, as offered on the Mini App upload page - carries
/// enough to render it (name, deadline, whether a submission already exists there) without the page needing
/// a second round-trip per project.</summary>
public sealed record PresenterProjectOption(int ProjectId, string ProjectName, DateTime? SubmissionDeadline, string? ExistingTitle);

/// <summary>Everything the Mini App upload page needs to render itself for one token - the presenter's
/// display name and every project they're currently approved for (empty if Admin revoked every approval
/// after the chat button was sent but before it was tapped).</summary>
public sealed record PresenterUploadContext(string FullName, IReadOnlyList<PresenterProjectOption> Projects);

/// <summary>What the confirmation message pushed back into the Telegram chat needs - the just-saved
/// presentation's project (for <see cref="TelegramBot.EventReminderFormatter.Format"/>) and title, and
/// whether this replaced an existing submission or created a new one.</summary>
public sealed record PresenterUploadSubmitResult(long ChatId, Project Project, string Title, bool IsUpdate);

/// <summary>Backs the Telegram Mini App page that is now the ONLY way a presenter submits a presentation -
/// picking the project, typing the title, and uploading the file all happen there (see
/// PresentationManager.API.Controllers.Web.PresenterController), over a plain HTTPS POST instead of through
/// the Telegram Bot API, whose own file downloads are capped at 20MB. Used from both processes that never
/// share memory: PresentationManager.BotService mints the token (<see cref="CreateTokenAsync"/>),
/// PresentationManager.API's PresenterController consumes it (<see cref="GetUploadContextAsync"/>/
/// <see cref="SubmitAsync"/>) - both only ever share the database.</summary>
public sealed class PresenterUploadService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    private readonly IPresenterUploadTokenRepository _tokenRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly PresentationQueueService _queueService;
    private readonly PresenterAssignmentService _presenterAssignmentService;
    private readonly ILogger<PresenterUploadService> _logger;

    public PresenterUploadService(
        IPresenterUploadTokenRepository tokenRepository, IProjectRepository projectRepository,
        ISettingsRepository settingsRepository, PresentationQueueService queueService,
        PresenterAssignmentService presenterAssignmentService, ILogger<PresenterUploadService> logger)
    {
        _tokenRepository = tokenRepository;
        _projectRepository = projectRepository;
        _settingsRepository = settingsRepository;
        _queueService = queueService;
        _presenterAssignmentService = presenterAssignmentService;
        _logger = logger;
    }

    /// <summary>Mints a fresh link token for this presenter - unlike the old per-project token, this only
    /// ever needs to identify WHO is uploading; the project and title are chosen on the page itself.</summary>
    public async Task<string> CreateTokenAsync(long chatId, int presenterId, string fullName, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        await _tokenRepository.AddAsync(new PresenterUploadToken
        {
            Token = token,
            ChatId = chatId,
            PresenterId = presenterId,
            FullName = fullName,
            ExpiresAt = DateTime.UtcNow.Add(TokenLifetime)
        }, ct);
        return token;
    }

    /// <summary>Null once expired - not single-use (unlike the old per-project token): a presenter may
    /// legitimately submit to more than one assigned project, or fix a mistake and resubmit, within the same
    /// link's window, so nothing here ever marks the token "used".</summary>
    private async Task<PresenterUploadToken?> GetValidTokenAsync(string token, CancellationToken ct = default)
    {
        var record = await _tokenRepository.GetByTokenAsync(token, ct);
        return record is null || record.ExpiresAt < DateTime.UtcNow ? null : record;
    }

    /// <summary>Null for "this link no longer works" (unknown/expired token) - the page must treat that the
    /// same way regardless of which it was, since neither is actionable beyond "go back to Telegram and tap
    /// the button again".</summary>
    public async Task<PresenterUploadContext?> GetUploadContextAsync(string token, CancellationToken ct = default)
    {
        var record = await GetValidTokenAsync(token, ct);
        if (record is null)
        {
            return null;
        }

        var projects = await _presenterAssignmentService.GetAssignedProjectsAsync(record.PresenterId, ct);
        var options = new List<PresenterProjectOption>(projects.Count);
        foreach (var project in projects)
        {
            var existing = await _queueService.GetByPresenterAndProjectAsync(project.Id, record.PresenterId, ct);
            options.Add(new PresenterProjectOption(project.Id, project.Name, project.SubmissionDeadline, existing?.Title));
        }

        return new PresenterUploadContext(record.FullName, options);
    }

    /// <summary>Saves the uploaded file into the same managed storage/queue the old in-chat flow used
    /// (<see cref="PresentationQueueService.AddAsync"/>/<see cref="PresentationQueueService.UpdateAsync"/>).
    /// Re-validates the token, the project assignment, and the project's submission deadline itself rather
    /// than trusting the page already did (defense-in-depth, same reasoning as
    /// <see cref="PresentationQueueService.AddAsync"/>'s own presenter-assignment re-check) - throws
    /// <see cref="InvalidOperationException"/> with a presenter-facing Uzbek message on any rejection.</summary>
    public async Task<PresenterUploadSubmitResult> SubmitAsync(
        string token, int projectId, string title, string sourceFilePath, PresentationFileType fileType, CancellationToken ct = default)
    {
        var record = await GetValidTokenAsync(token, ct)
            ?? throw new InvalidOperationException("Havola yaroqsiz yoki muddati o'tgan. Botga qaytib, qaytadan urinib ko'ring.");

        if (!await _presenterAssignmentService.IsAssignedAsync(projectId, record.PresenterId, ct))
        {
            throw new InvalidOperationException("Siz bu loyihaga biriktirilmagansiz.");
        }

        var project = await _projectRepository.GetByIdAsync(projectId, ct)
            ?? throw new InvalidOperationException("Bu loyiha endi mavjud emas.");
        if (project.SubmissionDeadline is { } deadline && DateTime.UtcNow > deadline)
        {
            throw new InvalidOperationException(
                $"Taqdimot topshirish/yangilash muddati tugagan ({deadline.ToLocalTime():dd.MM.yyyy HH:mm}). Fayl qabul qilinmadi.");
        }

        var settings = await _settingsRepository.GetAsync(ct);
        var existing = await _queueService.GetByPresenterAndProjectAsync(projectId, record.PresenterId, ct);
        var isUpdate = existing is not null;
        if (isUpdate)
        {
            // Left exactly as it already was, same reasoning as PresentationManagementForm.OnEditClick - a
            // resubmission replacing the file must not silently reset a value the project's own default (or
            // a since-made web override) already set.
            await _queueService.UpdateAsync(
                existing!.Id, record.FullName, title,
                settings.DefaultPresentationTimeSeconds, settings.DefaultDiscussionTimeSeconds,
                existing.ExtraDiscussionTimeSeconds, sourceFilePath, fileType, ct);
        }
        else
        {
            // Every presenter in the same project gets the project's own standard extra discussion time
            // (set once at project creation - see Project.ExtraDiscussionTimeSeconds) rather than asking for
            // it on each individual submission.
            await _queueService.AddAsync(
                projectId, record.FullName, title,
                sourceFilePath, fileType,
                settings.DefaultPresentationTimeSeconds, settings.DefaultDiscussionTimeSeconds,
                extraDiscussionTimeSeconds: project.ExtraDiscussionTimeSeconds, presenterId: record.PresenterId, ct: ct);
        }

        _logger.LogInformation(
            "Taqdimot mini-app orqali qabul qilindi: chat {ChatId}, loyiha {ProjectId}, sarlavha \"{Title}\"",
            record.ChatId, projectId, title);
        return new PresenterUploadSubmitResult(record.ChatId, project, title, isUpdate);
    }
}
