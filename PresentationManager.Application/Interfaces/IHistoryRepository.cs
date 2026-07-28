using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IHistoryRepository
{
    Task AddAsync(HistoryEntry entry, CancellationToken ct = default);

    Task<List<HistoryEntry>> GetRecentAsync(int count = 200, CancellationToken ct = default);

    Task<List<HistoryEntry>> GetForPresentationAsync(int presentationId, CancellationToken ct = default);
}
