using Microsoft.Extensions.Logging;
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
    private readonly ILogger<FileStorageService>? _logger;

    /// <param name="storageRoot">Absolute path to the folder files are stored under — the caller decides
    /// where that lives (e.g. per-user AppData, so a published single-file .exe has nothing else to sit
    /// alongside it) rather than this service assuming it's always next to the executable.</param>
    /// <param name="logger">Optional (not resolved via constructor DI - PresentationManager.API/BotService
    /// both build this via a factory delegate so they can pass <paramref name="storageRoot"/>, so this is
    /// passed through explicitly from that same factory instead of relying on activation).</param>
    public FileStorageService(string storageRoot, ILogger<FileStorageService>? logger = null)
    {
        _storageRoot = storageRoot;
        _logger = logger;
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<string> SaveFileAsync(string sourceFilePath, CancellationToken ct = default)
    {
        var dayFolderName = DateTime.Now.ToString("yyyy-MM-dd");
        Directory.CreateDirectory(Path.Combine(_storageRoot, dayFolderName));

        var extension = Path.GetExtension(sourceFilePath);
        var fileName = $"{Guid.NewGuid():N}_{Path.GetFileNameWithoutExtension(sourceFilePath)}{extension}";
        // Always '/' regardless of OS (not Path.Combine, which would use '\' on Windows) - this value now
        // also doubles as a URL path segment for PresentationManager.API's FilesController, and travels
        // between a Windows desktop client and a Linux server, so it needs one consistent separator rather
        // than whichever OS happened to create the file.
        var relativePath = $"{dayFolderName}/{fileName}";
        var destinationPath = Path.Combine(_storageRoot, dayFolderName, fileName);

        try
        {
            await using (var source = File.OpenRead(sourceFilePath))
            await using (var destination = File.Create(destinationPath))
            {
                await source.CopyToAsync(destination, ct);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Faylni saqlashda xatolik: {SourcePath} -> {RelativePath}", sourceFilePath, relativePath);
            throw;
        }

        _logger?.LogInformation("Fayl saqlandi: {RelativePath}", relativePath);
        return relativePath;
    }

    public Task DeleteFileAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_storageRoot, relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger?.LogInformation("Fayl o'chirildi: {RelativePath}", relativePath);
        }
        else
        {
            _logger?.LogWarning("O'chirish uchun fayl topilmadi: {RelativePath}", relativePath);
        }

        return Task.CompletedTask;
    }

    public Task<string> GetAbsolutePathAsync(string relativePath, CancellationToken ct = default) =>
        Task.FromResult(Path.Combine(_storageRoot, relativePath));
}
