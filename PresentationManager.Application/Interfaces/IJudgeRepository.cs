using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IJudgeRepository
{
    Task<List<Judge>> GetAllAsync(CancellationToken ct = default);

    Task<List<Judge>> GetByProjectIdAsync(int projectId, CancellationToken ct = default);

    /// <summary>Every judge whose project belongs to <paramref name="organizationId"/>, plus any judge whose
    /// project has no recorded organization (legacy rows) - a Manager's dashboard's org-scoped counterpart to
    /// <see cref="GetAllAsync"/>. See <see cref="Domain.Entities.Project.OrganizationId"/>.</summary>
    Task<List<Judge>> GetByOrganizationAsync(int organizationId, CancellationToken ct = default);

    /// <summary>Every judge row already linked to this Telegram chat — one per project they judge.</summary>
    Task<List<Judge>> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default);

    Task<Judge> AddAsync(Judge judge, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
