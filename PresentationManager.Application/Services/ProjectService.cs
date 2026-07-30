using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Services;

/// <summary>Admin-facing CRUD over projects. Deleting a project also deletes every presentation that
/// belongs to it — including their stored files, which live outside the database and so can't be cleaned
/// up by the DB-level cascade delete alone.</summary>
public sealed class ProjectService
{
    private readonly IProjectRepository _projectRepository;
    private readonly IPresentationRepository _presentationRepository;
    private readonly IFileStorageService _fileStorageService;

    public ProjectService(
        IProjectRepository projectRepository,
        IPresentationRepository presentationRepository,
        IFileStorageService fileStorageService)
    {
        _projectRepository = projectRepository;
        _presentationRepository = presentationRepository;
        _fileStorageService = fileStorageService;
    }

    public Task<List<Project>> GetAllAsync(CancellationToken ct = default) =>
        _projectRepository.GetAllAsync(ct);

    public async Task<Project> CreateAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Loyiha nomi bo'sh bo'lishi mumkin emas.");
        }

        var project = new Project { Name = name.Trim() };
        return await _projectRepository.AddAsync(project, ct);
    }

    /// <summary>Returns how many presentations currently belong to <paramref name="projectId"/> — used by the
    /// UI to warn the operator before they confirm a delete that will take those presentations down with it.</summary>
    public async Task<int> CountPresentationsAsync(int projectId, CancellationToken ct = default) =>
        (await _presentationRepository.GetByProjectIdAsync(projectId, ct)).Count;

    public async Task DeleteAsync(int projectId, CancellationToken ct = default)
    {
        var presentations = await _presentationRepository.GetByProjectIdAsync(projectId, ct);
        foreach (var presentation in presentations)
        {
            _fileStorageService.DeleteFile(presentation.FilePath);
        }

        // The Presentations rows themselves are removed by the DB cascade configured on the ProjectId FK.
        await _projectRepository.DeleteAsync(projectId, ct);
    }
}
