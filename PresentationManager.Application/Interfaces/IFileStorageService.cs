namespace PresentationManager.Application.Interfaces;

public interface IFileStorageService
{
    /// <summary>Copies the source file into the managed storage folder and returns the stored relative path.</summary>
    Task<string> SaveFileAsync(string sourceFilePath, CancellationToken ct = default);

    /// <summary>Async (unlike the local-disk implementation strictly needs) because
    /// PresentationManager.ApiClient's HTTP-backed implementation has to make a network call - see
    /// HttpFileStorageService, and PresentationManager.UI's CLAUDE.md's own "UI must never freeze... Always
    /// asynchronous" rule, which a blocking synchronous download would violate.</summary>
    Task DeleteFileAsync(string relativePath, CancellationToken ct = default);

    /// <summary>Resolves a stored relative path to a real local file path this process can open directly
    /// (PowerPoint COM automation, the OS's default viewer, ...). For the HTTP-backed implementation this
    /// downloads and caches the file locally on first access - see HttpFileStorageService.</summary>
    Task<string> GetAbsolutePathAsync(string relativePath, CancellationToken ct = default);
}
