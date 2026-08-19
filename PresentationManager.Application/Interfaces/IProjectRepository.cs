using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IProjectRepository
{
    Task<List<Project>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Every project created by <paramref name="createdByUserId"/>, plus any project with no
    /// recorded creator (legacy rows from before this field existed) - see <see cref="Project.CreatedByUserId"/>.</summary>
    Task<List<Project>> GetByCreatorAsync(int createdByUserId, CancellationToken ct = default);

    Task<Project?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<Project> AddAsync(Project project, CancellationToken ct = default);

    Task UpdateAsync(Project project, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
