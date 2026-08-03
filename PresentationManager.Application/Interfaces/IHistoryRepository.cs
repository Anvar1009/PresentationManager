using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IHistoryRepository
{
    Task AddAsync(HistoryEntry entry, CancellationToken ct = default);

    Task<List<HistoryEntry>> GetRecentAsync(int count = 200, CancellationToken ct = default);

    Task<List<HistoryEntry>> GetForPresentationAsync(int presentationId, CancellationToken ct = default);

    /// <summary>SuperAdmin panel's "Jurnalni tozalash" action — this table grows unbounded (every queue/timer
    /// event ever logged), so it's the only one in the app with a bulk-delete escape hatch.</summary>
    Task ClearAllAsync(CancellationToken ct = default);
}
