using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IPresenterProjectAssignmentRepository
{
    Task<List<PresenterProjectAssignment>> GetByProjectIdAsync(int projectId, CancellationToken ct = default);

    Task<List<PresenterProjectAssignment>> GetByPresenterIdAsync(int presenterId, CancellationToken ct = default);

    Task<bool> ExistsAsync(int projectId, int presenterId, CancellationToken ct = default);

    Task<PresenterProjectAssignment> AddAsync(PresenterProjectAssignment assignment, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
