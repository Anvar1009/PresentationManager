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

    public PresentationQueueService(IPresentationRepository presentationRepository, IFileStorageService fileStorageService)
    {
        _presentationRepository = presentationRepository;
        _fileStorageService = fileStorageService;
    }

    public Task<List<Presentation>> GetAllAsync(CancellationToken ct = default) =>
        _presentationRepository.GetAllOrderedAsync(ct);

    public Task<List<Presentation>> SearchAsync(string query, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(query)
            ? _presentationRepository.GetAllOrderedAsync(ct)
            : _presentationRepository.SearchByNameAsync(query, ct);

    public async Task<Presentation> AddAsync(
        string fullName, string title,
        string sourceFilePath, PresentationFileType fileType,
        int presentationTimeSeconds, int discussionTimeSeconds, CancellationToken ct = default)
    {
        var storedRelativePath = await _fileStorageService.SaveFileAsync(sourceFilePath, ct);
        var all = await _presentationRepository.GetAllOrderedAsync(ct);

        var presentation = new Presentation
        {
            FullName = fullName,
            Title = title,
            FilePath = storedRelativePath,
            FileType = fileType,
            OrderNumber = all.Count,
            PresentationTimeSeconds = presentationTimeSeconds,
            DiscussionTimeSeconds = discussionTimeSeconds,
            Status = PresentationStatus.Waiting
        };

        return await _presentationRepository.AddAsync(presentation, ct);
    }

    /// <summary>Edits presenter/timing fields. Pass a new <paramref name="sourceFilePath"/> only when the
    /// operator picked a replacement file; the old stored file is deleted once the new one is saved.</summary>
    public async Task UpdateAsync(
        int id, string fullName, string title,
        int presentationTimeSeconds, int discussionTimeSeconds,
        string? sourceFilePath, PresentationFileType? fileType, CancellationToken ct = default)
    {
        var presentation = await _presentationRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Presentation {id} not found.");

        presentation.FullName = fullName;
        presentation.Title = title;
        presentation.PresentationTimeSeconds = presentationTimeSeconds;
        presentation.DiscussionTimeSeconds = discussionTimeSeconds;
        presentation.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(sourceFilePath) && fileType is not null)
        {
            var oldPath = presentation.FilePath;
            presentation.FilePath = await _fileStorageService.SaveFileAsync(sourceFilePath, ct);
            presentation.FileType = fileType.Value;
            _fileStorageService.DeleteFile(oldPath);
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
        _fileStorageService.DeleteFile(presentation.FilePath);
    }

    /// <summary>Applies a new drag-and-drop order. <paramref name="orderedIds"/> must contain every
    /// presentation Id currently in the queue, in its new display order.</summary>
    public Task ReorderAsync(IReadOnlyList<int> orderedIds, CancellationToken ct = default) =>
        _presentationRepository.ReorderAsync(orderedIds, ct);
}
