using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IPresentationRepository
{
    Task<List<Presentation>> GetAllOrderedAsync(CancellationToken ct = default);

    Task<Presentation?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<Presentation> AddAsync(Presentation presentation, CancellationToken ct = default);

    Task UpdateAsync(Presentation presentation, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Persists a full reorder: index in the list becomes the new OrderNumber for that presentation Id.</summary>
    Task ReorderAsync(IReadOnlyList<int> orderedPresentationIds, CancellationToken ct = default);

    Task<List<Presentation>> SearchByNameAsync(string query, CancellationToken ct = default);
}
