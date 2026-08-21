using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Services;

/// <summary>A person must register through the Telegram bot (name + shared contact, becoming a
/// <see cref="Presenter"/> row) BEFORE Admin can assign them as a judge — Admin picks from that
/// already-registered list rather than typing a phone number blind, so the resulting <see cref="Judge"/> row
/// is always immediately linked to a real Telegram chat (<see cref="Judge.TelegramChatId"/> is never null
/// under this flow). <see cref="JudgeAssigned"/> lets the Telegram bot (which has no reason to otherwise
/// know this just happened) push the new judge a notification the moment Admin assigns them, instead of
/// them having to guess and press /start again on their own.</summary>
public sealed class JudgeService
{
    private readonly IJudgeRepository _judgeRepository;
    private readonly IPresenterRepository _presenterRepository;
    private readonly ILogger<JudgeService> _logger;

    public event Action<Judge>? JudgeAssigned;

    public JudgeService(IJudgeRepository judgeRepository, IPresenterRepository presenterRepository, ILogger<JudgeService> logger)
    {
        _judgeRepository = judgeRepository;
        _presenterRepository = presenterRepository;
        _logger = logger;
    }

    public Task<List<Judge>> GetAllAsync(CancellationToken ct = default) => _judgeRepository.GetAllAsync(ct);

    public Task<List<Judge>> GetByProjectIdAsync(int projectId, CancellationToken ct = default) =>
        _judgeRepository.GetByProjectIdAsync(projectId, ct);

    /// <summary>Assigns an already bot-registered person (<paramref name="presenterId"/> - the
    /// <see cref="Presenter"/> row created when they shared their contact) as a judge for a project.</summary>
    public async Task<Judge> AssignAsync(int projectId, int presenterId, CancellationToken ct = default)
    {
        var presenter = await _presenterRepository.GetByIdAsync(presenterId, ct);
        if (presenter is null)
        {
            _logger.LogWarning("Hakam tayinlashga urinish rad etildi: presenter {PresenterId} topilmadi.", presenterId);
            throw new InvalidOperationException("Bu odam topilmadi - avval botga /start bosib ro'yxatdan o'tishi kerak.");
        }

        if (string.IsNullOrWhiteSpace(presenter.PhoneNumber))
        {
            _logger.LogWarning("Hakam tayinlashga urinish rad etildi: presenter {PresenterId} telefon raqami yo'q.", presenterId);
            throw new InvalidOperationException("Bu odamning telefon raqami yo'q.");
        }

        var existingForProject = await _judgeRepository.GetByProjectIdAsync(projectId, ct);
        if (existingForProject.Any(j => j.TelegramChatId == presenter.TelegramChatId))
        {
            _logger.LogWarning(
                "Hakam tayinlashga urinish rad etildi: presenter {PresenterId} loyihaga {ProjectId} allaqachon hakam.",
                presenterId, projectId);
            throw new InvalidOperationException("Bu odam allaqachon shu loyihaga hakam etib tayinlangan.");
        }

        var judge = await _judgeRepository.AddAsync(new Judge
        {
            ProjectId = projectId,
            PhoneNumber = presenter.PhoneNumber,
            FullName = presenter.FullName,
            TelegramChatId = presenter.TelegramChatId
        }, ct);

        _logger.LogInformation("Hakam tayinlandi: {JudgeId} - {FullName} (loyiha {ProjectId})",
            judge.Id, judge.FullName, projectId);
        JudgeAssigned?.Invoke(judge);
        return judge;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await _judgeRepository.DeleteAsync(id, ct);
        _logger.LogInformation("Hakam o'chirildi: {JudgeId}", id);
    }

    public Task<List<Judge>> GetLinkedAssignmentsByChatIdAsync(long telegramChatId, CancellationToken ct = default) =>
        _judgeRepository.GetByTelegramChatIdAsync(telegramChatId, ct);
}
