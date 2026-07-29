using PresentationManager.Application.Interfaces;

namespace PresentationManager.Infrastructure.Services;

/// <summary>Copies picked presentation files into a managed storage folder, under a per-day subfolder
/// ("yyyy-MM-dd/"), so the database only ever stores a relative path — never the file bytes themselves —
/// and files stay organized by the day they were added. Presentations are never pruned by date:
/// <see cref="Repositories.PresentationRepository.GetAllOrderedAsync"/> always returns every row regardless
/// of which day's folder its file lives in, so anything added today is still listed (and its file still
/// resolvable) the next time the app starts, tomorrow or later.</summary>
public class FileStorageService : IFileStorageService
{
    private readonly string _storageRoot;

    /// <param name="storageRoot">Absolute path to the folder files are stored under — the caller decides
    /// where that lives (e.g. per-user AppData, so a published single-file .exe has nothing else to sit
    /// alongside it) rather than this service assuming it's always next to the executable.</param>
    public FileStorageService(string storageRoot)
    {
        _storageRoot = storageRoot;
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<string> SaveFileAsync(string sourceFilePath, CancellationToken ct = default)
    {
        var dayFolderName = DateTime.Now.ToString("yyyy-MM-dd");
        Directory.CreateDirectory(Path.Combine(_storageRoot, dayFolderName));

        var extension = Path.GetExtension(sourceFilePath);
        var fileName = $"{Guid.NewGuid():N}_{Path.GetFileNameWithoutExtension(sourceFilePath)}{extension}";
        var relativePath = Path.Combine(dayFolderName, fileName);
        var destinationPath = Path.Combine(_storageRoot, relativePath);

        await using (var source = File.OpenRead(sourceFilePath))
        await using (var destination = File.Create(destinationPath))
        {
            await source.CopyToAsync(destination, ct);
        }

        return relativePath;
    }

    public void DeleteFile(string relativePath)
    {
        var fullPath = GetAbsolutePath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public string GetAbsolutePath(string relativePath) => Path.Combine(_storageRoot, relativePath);
}
