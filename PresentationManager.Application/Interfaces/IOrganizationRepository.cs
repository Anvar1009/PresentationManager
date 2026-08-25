using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IOrganizationRepository
{
    Task<List<Organization>> GetAllAsync(CancellationToken ct = default);

    Task<Organization?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<Organization> AddAsync(Organization organization, CancellationToken ct = default);

    Task RenameAsync(int id, string name, CancellationToken ct = default);
}
