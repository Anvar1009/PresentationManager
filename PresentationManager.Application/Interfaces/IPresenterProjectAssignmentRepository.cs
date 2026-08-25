using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IPresenterProjectAssignmentRepository
{
    Task<List<PresenterProjectAssignment>> GetByProjectIdAsync(int projectId, CancellationToken ct = default);

    Task<List<PresenterProjectAssignment>> GetByPresenterIdAsync(int presenterId, CancellationToken ct = default);

    /// <summary>Every assignment whose project belongs to <paramref name="organizationId"/>, plus any whose
    /// project has no recorded organization (legacy rows) - lets a Manager's "Taqdimotchilar" page derive the
    /// distinct presenters approved for its own organization's projects. See
    /// <see cref="Domain.Entities.Project.OrganizationId"/>.</summary>
    Task<List<PresenterProjectAssignment>> GetByOrganizationAsync(int organizationId, CancellationToken ct = default);

    Task<bool> ExistsAsync(int projectId, int presenterId, CancellationToken ct = default);

    Task<PresenterProjectAssignment> AddAsync(PresenterProjectAssignment assignment, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
