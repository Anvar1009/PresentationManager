using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Services;

/// <summary>A person must register through the Telegram bot (becoming a <see cref="Presenter"/> row) BEFORE
/// Admin can approve them for a project — Admin picks from that already-registered list in the Admin panel,
/// and the resulting <see cref="PresenterProjectAssignment"/> row's mere existence IS the approval: no
/// separate status flag, and the Telegram notification is pushed server-side, from
/// <c>PresentationManager.API.Controllers.PresenterAssignmentsController.Add</c>, the moment it's created
/// (mirrors <see cref="JudgeService"/>'s equivalent, except that one still fires an in-process event because
/// a judge can also be assigned from the bot's own linked-Admin mirror — presenter approval only ever happens
/// from the desktop Admin panel, i.e. over HTTP, so this service has no client-side event to raise).</summary>
public sealed class PresenterAssignmentService
{
    private readonly IPresenterProjectAssignmentRepository _assignmentRepository;
    private readonly IPresenterRepository _presenterRepository;
    private readonly IProjectRepository _projectRepository;

    public PresenterAssignmentService(
        IPresenterProjectAssignmentRepository assignmentRepository,
        IPresenterRepository presenterRepository,
        IProjectRepository projectRepository)
    {
        _assignmentRepository = assignmentRepository;
        _presenterRepository = presenterRepository;
        _projectRepository = projectRepository;
    }

    public Task<List<PresenterProjectAssignment>> GetByProjectIdAsync(int projectId, CancellationToken ct = default) =>
        _assignmentRepository.GetByProjectIdAsync(projectId, ct);

    /// <summary>Every project this presenter has been approved for — the Telegram bot's upload flow filters
    /// its project picker down to exactly this list (see <c>PresentationBotHostedService.ShowProjectListAsync</c>).
    /// A project deleted after the assignment was made is silently skipped rather than surfaced as an error.</summary>
    public async Task<List<Project>> GetAssignedProjectsAsync(int presenterId, CancellationToken ct = default)
    {
        var assignments = await _assignmentRepository.GetByPresenterIdAsync(presenterId, ct);
        var projects = new List<Project>();
        foreach (var assignment in assignments)
        {
            var project = await _projectRepository.GetByIdAsync(assignment.ProjectId, ct);
            if (project is not null)
            {
                projects.Add(project);
            }
        }

        return projects;
    }

    public Task<bool> IsAssignedAsync(int projectId, int presenterId, CancellationToken ct = default) =>
        _assignmentRepository.ExistsAsync(projectId, presenterId, ct);

    /// <summary>Approves an already bot-registered person (<paramref name="presenterId"/>) to take part in a
    /// project — called only from the Admin panel (<c>PresenterAssignmentsController.Add</c>).</summary>
    public async Task<PresenterProjectAssignment> AssignAsync(int projectId, int presenterId, CancellationToken ct = default)
    {
        _ = await _presenterRepository.GetByIdAsync(presenterId, ct)
            ?? throw new InvalidOperationException("Bu odam topilmadi - avval botga /start bosib ro'yxatdan o'tishi kerak.");

        if (await _assignmentRepository.ExistsAsync(projectId, presenterId, ct))
        {
            throw new InvalidOperationException("Bu odam allaqachon shu loyihaga biriktirilgan.");
        }

        return await _assignmentRepository.AddAsync(new PresenterProjectAssignment
        {
            ProjectId = projectId,
            PresenterId = presenterId
        }, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct = default) => _assignmentRepository.DeleteAsync(id, ct);
}
