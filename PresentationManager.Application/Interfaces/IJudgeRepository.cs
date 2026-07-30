using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IJudgeRepository
{
    Task<List<Judge>> GetAllAsync(CancellationToken ct = default);

    Task<List<Judge>> GetByProjectIdAsync(int projectId, CancellationToken ct = default);

    /// <summary>Every judge row already linked to this Telegram chat — one per project they judge.</summary>
    Task<List<Judge>> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default);

    Task<Judge> AddAsync(Judge judge, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
