namespace PresentationManager.Application.Interfaces;

public interface IFileStorageService
{
    /// <summary>Copies the source file into the managed storage folder and returns the stored relative path.</summary>
    Task<string> SaveFileAsync(string sourceFilePath, CancellationToken ct = default);

    void DeleteFile(string relativePath);

    string GetAbsolutePath(string relativePath);
}
