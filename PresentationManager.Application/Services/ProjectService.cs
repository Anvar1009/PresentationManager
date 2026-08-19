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
    private readonly IPresenterRepository _presenterRepository;
    private readonly IFileStorageService _fileStorageService;

    public ProjectService(
        IProjectRepository projectRepository,
        IPresentationRepository presentationRepository,
        IPresenterRepository presenterRepository,
        IFileStorageService fileStorageService)
    {
        _projectRepository = projectRepository;
        _presentationRepository = presentationRepository;
        _presenterRepository = presenterRepository;
        _fileStorageService = fileStorageService;
    }

    public Task<List<Project>> GetAllAsync(CancellationToken ct = default) =>
        _projectRepository.GetAllAsync(ct);

    /// <summary>Scoped project list for the Admin panel - see <see cref="Project.CreatedByUserId"/>. Every
    /// other caller (Operator's own "Loyihalar" dialog, SuperAdmin, the Telegram bot) keeps using the
    /// unscoped <see cref="GetAllAsync"/>, since only the Admin role's project list is meant to be
    /// per-creator.</summary>
    public Task<List<Project>> GetByCreatorAsync(int createdByUserId, CancellationToken ct = default) =>
        _projectRepository.GetByCreatorAsync(createdByUserId, ct);

    public async Task<Project> CreateAsync(
        string name, DateOnly eventStartDate, DateOnly eventEndDate, TimeOnly? eventTime, string? location,
        int? createdByUserId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Loyiha nomi bo'sh bo'lishi mumkin emas.");
        }

        if (eventEndDate < eventStartDate)
        {
            throw new InvalidOperationException("Tugash sanasi boshlanish sanasidan oldin bo'lishi mumkin emas.");
        }

        var project = new Project
        {
            Name = name.Trim(),
            EventStartDate = eventStartDate,
            EventEndDate = eventEndDate,
            EventTime = eventTime,
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            CreatedByUserId = createdByUserId
        };
        return await _projectRepository.AddAsync(project, ct);
    }

    /// <summary>Admin-facing "Taqdimot topshirish muddati" control - the Telegram bot checks this before
    /// accepting a new submission or a file/title update to an existing one (see
    /// <c>PresentationBotHostedService.HandleDocumentAsync</c>). Pass null to clear a previously-set
    /// deadline.</summary>
    public async Task SetSubmissionDeadlineAsync(int projectId, DateTime? deadlineUtc, CancellationToken ct = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, ct)
            ?? throw new InvalidOperationException("Loyiha topilmadi.");

        project.SubmissionDeadline = deadlineUtc;
        project.UpdatedAt = DateTime.UtcNow;
        await _projectRepository.UpdateAsync(project, ct);
    }

    /// <summary>Returns how many presentations currently belong to <paramref name="projectId"/> — used by the
    /// UI to warn the operator before they confirm a delete that will take those presentations down with it.</summary>
    public async Task<int> CountPresentationsAsync(int projectId, CancellationToken ct = default) =>
        (await _presentationRepository.GetByProjectIdAsync(projectId, ct)).Count;

    /// <summary>Every distinct presenter who has submitted at least one presentation to this project — the
    /// Admin panel's "Qatnashchilar" table. Phone numbers only show up for presenters who registered through
    /// the Telegram bot (<see cref="Presentation.PresenterId"/> is null for anything the operator added by
    /// hand, so those just fall back to showing the plain <see cref="Presentation.FullName"/> text with no
    /// phone).</summary>
    public async Task<List<ProjectParticipant>> GetParticipantsAsync(int projectId, CancellationToken ct = default)
    {
        var presentations = await _presentationRepository.GetByProjectIdAsync(projectId, ct);
        var presenters = await _presenterRepository.GetAllAsync(ct);
        var presentersById = presenters.ToDictionary(p => p.Id);

        return presentations
            .GroupBy(p => p.PresenterId is int presenterId && presentersById.TryGetValue(presenterId, out var presenter)
                ? presenter.FullName
                : p.FullName)
            .Select(group => new ProjectParticipant
            {
                FullName = group.Key,
                PhoneNumber = group
                    .Select(p => p.PresenterId is int id && presentersById.TryGetValue(id, out var presenter) ? presenter.PhoneNumber : null)
                    .FirstOrDefault(phone => phone is not null),
                PresentationCount = group.Count()
            })
            .OrderBy(p => p.FullName)
            .ToList();
    }

    public async Task DeleteAsync(int projectId, CancellationToken ct = default)
    {
        var presentations = await _presentationRepository.GetByProjectIdAsync(projectId, ct);
        foreach (var presentation in presentations)
        {
            await _fileStorageService.DeleteFileAsync(presentation.FilePath, ct);
        }

        // The Presentations rows themselves are removed by the DB cascade configured on the ProjectId FK.
        await _projectRepository.DeleteAsync(projectId, ct);
    }
}
