using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IHistoryRepository
{
    Task AddAsync(HistoryEntry entry, CancellationToken ct = default);

    Task<List<HistoryEntry>> GetRecentAsync(int count = 200, CancellationToken ct = default);

    /// <summary>Org-scoped counterpart to <see cref="GetRecentAsync"/> - the most recent entries whose
    /// presentation's project belongs to <paramref name="organizationId"/> (plus legacy rows with no recorded
    /// organization), for a Manager's "Jurnal" page. See <see cref="Domain.Entities.Project.OrganizationId"/>.</summary>
    Task<List<HistoryEntry>> GetRecentByOrganizationAsync(int organizationId, int count = 200, CancellationToken ct = default);

    Task<List<HistoryEntry>> GetForPresentationAsync(int presentationId, CancellationToken ct = default);

    /// <summary>SuperAdmin panel's "Jurnalni tozalash" action — this table grows unbounded (every queue/timer
    /// event ever logged), so it's the only one in the app with a bulk-delete escape hatch.</summary>
    Task ClearAllAsync(CancellationToken ct = default);
}
