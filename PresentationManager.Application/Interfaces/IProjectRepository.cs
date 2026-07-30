using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IProjectRepository
{
    Task<List<Project>> GetAllAsync(CancellationToken ct = default);

    Task<Project?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<Project> AddAsync(Project project, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
