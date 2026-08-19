using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.Application.Services;

/// <summary>Admin-facing CRUD, reordering and search over the presentation queue. Copies the picked file
/// into managed storage on add, and removes it on delete.</summary>
public sealed class PresentationQueueService
{
    private readonly IPresentationRepository _presentationRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IPresenterProjectAssignmentRepository _assignmentRepository;

    public PresentationQueueService(
        IPresentationRepository presentationRepository,
        IFileStorageService fileStorageService,
        IPresenterProjectAssignmentRepository assignmentRepository)
    {
        _presentationRepository = presentationRepository;
        _fileStorageService = fileStorageService;
        _assignmentRepository = assignmentRepository;
    }

    public Task<List<Presentation>> GetAllAsync(int projectId, CancellationToken ct = default) =>
        _presentationRepository.GetAllOrderedAsync(projectId, ct);

    /// <summary>Every presentation across every project — for the SuperAdmin panel's read-only overview.</summary>
    public Task<List<Presentation>> GetAllAsync(CancellationToken ct = default) =>
        _presentationRepository.GetAllAsync(ct);

    public Task<List<Presentation>> SearchAsync(string query, int projectId, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(query)
            ? _presentationRepository.GetAllOrderedAsync(projectId, ct)
            : _presentationRepository.SearchByNameAsync(query, projectId, ct);

    /// <summary>This presenter's existing submission to this project, if any - the Telegram bot's upload flow
    /// checks this before accepting a new file, so a second submission to the same project updates that one
    /// (<see cref="UpdateAsync"/>) instead of creating a duplicate queue entry (see
    /// <c>PresentationBotHostedService.HandleProjectSelectionCallbackAsync</c>/<c>HandleDocumentAsync</c>).</summary>
    public async Task<Presentation?> GetByPresenterAndProjectAsync(int projectId, int presenterId, CancellationToken ct = default)
    {
        var presentations = await _presentationRepository.GetAllOrderedAsync(projectId, ct);
        return presentations.FirstOrDefault(p => p.PresenterId == presenterId);
    }

    public async Task<Presentation> AddAsync(
        int projectId, string fullName, string title,
        string sourceFilePath, PresentationFileType fileType,
        int presentationTimeSeconds, int discussionTimeSeconds,
        int extraDiscussionTimeSeconds = 0,
        int? presenterId = null, CancellationToken ct = default)
    {
        // Defense-in-depth: the Telegram bot itself only ever offers projects a presenter is approved for
        // (see PresentationBotHostedService.ShowProjectListAsync), but this re-checks server-side in case an
        // approval was revoked mid-session or a stale/forged callback slipped through. Operator-added
        // presentations (presenterId null - no bot registration to check against) are unaffected.
        if (presenterId is { } id && !await _assignmentRepository.ExistsAsync(projectId, id, ct))
        {
            throw new InvalidOperationException("Siz bu loyihaga hali biriktirilmagansiz.");
        }

        var storedRelativePath = await _fileStorageService.SaveFileAsync(sourceFilePath, ct);
        var existing = await _presentationRepository.GetAllOrderedAsync(projectId, ct);

        var presentation = new Presentation
        {
            ProjectId = projectId,
            PresenterId = presenterId,
            FullName = fullName,
            Title = title,
            FilePath = storedRelativePath,
            FileType = fileType,
            OrderNumber = existing.Count,
            PresentationTimeSeconds = presentationTimeSeconds,
            DiscussionTimeSeconds = discussionTimeSeconds,
            ExtraDiscussionTimeSeconds = extraDiscussionTimeSeconds,
            Status = PresentationStatus.Waiting
        };

        return await _presentationRepository.AddAsync(presentation, ct);
    }

    /// <summary>Edits presenter/timing fields. Pass a new <paramref name="sourceFilePath"/> only when the
    /// operator picked a replacement file; the old stored file is deleted once the new one is saved.</summary>
    public async Task UpdateAsync(
        int id, string fullName, string title,
        int presentationTimeSeconds, int discussionTimeSeconds, int extraDiscussionTimeSeconds,
        string? sourceFilePath, PresentationFileType? fileType, CancellationToken ct = default)
    {
        var presentation = await _presentationRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Presentation {id} not found.");

        presentation.FullName = fullName;
        presentation.Title = title;
        presentation.PresentationTimeSeconds = presentationTimeSeconds;
        presentation.DiscussionTimeSeconds = discussionTimeSeconds;
        presentation.ExtraDiscussionTimeSeconds = extraDiscussionTimeSeconds;
        presentation.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(sourceFilePath) && fileType is not null)
        {
            var oldPath = presentation.FilePath;
            presentation.FilePath = await _fileStorageService.SaveFileAsync(sourceFilePath, ct);
            presentation.FileType = fileType.Value;
            await _fileStorageService.DeleteFileAsync(oldPath, ct);
        }

        await _presentationRepository.UpdateAsync(presentation, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var presentation = await _presentationRepository.GetByIdAsync(id, ct);
        if (presentation is null)
        {
            return;
        }

        await _presentationRepository.DeleteAsync(id, ct);
        await _fileStorageService.DeleteFileAsync(presentation.FilePath, ct);
    }

    /// <summary>Applies a new drag-and-drop order. <paramref name="orderedIds"/> must contain every
    /// presentation Id currently in the queue, in its new display order.</summary>
    public Task ReorderAsync(IReadOnlyList<int> orderedIds, CancellationToken ct = default) =>
        _presentationRepository.ReorderAsync(orderedIds, ct);

    /// <summary>"Tartib operatori" role's one action — shuffles a project's presentation order at random and
    /// persists it via <see cref="ReorderAsync"/>. Fisher-Yates, uniform over all permutations.</summary>
    public async Task RandomizeOrderAsync(int projectId, CancellationToken ct = default)
    {
        var current = await _presentationRepository.GetAllOrderedAsync(projectId, ct);
        var ids = current.Select(p => p.Id).ToList();

        var rng = Random.Shared;
        for (var i = ids.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (ids[i], ids[j]) = (ids[j], ids[i]);
        }

        await _presentationRepository.ReorderAsync(ids, ct);
    }

    /// <summary>Undoes any number of test draws by restoring the "default" order - alphabetical by full name
    /// - the OrderOperator's "Jadvalni tozalash" button on the randomize stage, for resetting the queue back
    /// to a clean, predictable slate after rehearsing the draw and before running it for real. Same
    /// ReorderAsync/OrderNumber mechanism as <see cref="RandomizeOrderAsync"/>, just with a deterministic
    /// ordering instead of a shuffled one.</summary>
    public async Task ResetOrderAsync(int projectId, CancellationToken ct = default)
    {
        var current = await _presentationRepository.GetAllOrderedAsync(projectId, ct);
        var ids = current
            .OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id)
            .Select(p => p.Id)
            .ToList();

        await _presentationRepository.ReorderAsync(ids, ct);
    }
}
