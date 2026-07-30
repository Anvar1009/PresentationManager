using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IPresentationRepository
{
    Task<List<Presentation>> GetAllOrderedAsync(int projectId, CancellationToken ct = default);

    Task<Presentation?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<Presentation> AddAsync(Presentation presentation, CancellationToken ct = default);

    Task UpdateAsync(Presentation presentation, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Persists a full reorder: index in the list becomes the new OrderNumber for that presentation Id.</summary>
    Task ReorderAsync(IReadOnlyList<int> orderedPresentationIds, CancellationToken ct = default);

    Task<List<Presentation>> SearchByNameAsync(string query, int projectId, CancellationToken ct = default);

    /// <summary>Every presentation belonging to a project, unordered — used when a project is deleted so its
    /// files can be cleaned up before the DB cascade removes the rows.</summary>
    Task<List<Presentation>> GetByProjectIdAsync(int projectId, CancellationToken ct = default);
}
