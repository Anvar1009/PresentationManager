using System.Net.Http.Json;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.ApiClient;

/// <summary>Uploads to / downloads from PresentationManager.API's FilesController instead of touching a
/// shared network folder. Downloaded bytes are cached under %LocalAppData%\PresentationManager\Cache so
/// PowerPoint COM automation (which can only ever open a real local file, never a URL) and repeated
/// previews of the same presentation don't re-download every time - safe because the stored relative path
/// already embeds a GUID (see FileStorageService.SaveFileAsync), so content at a given path never changes
/// underneath an already-cached copy.</summary>
public sealed class HttpFileStorageService : IFileStorageService
{
    private readonly HttpClient _http;
    private readonly string _cacheRoot;

    public HttpFileStorageService(HttpClient http)
    {
        _http = http;
        _cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PresentationManager", "Cache");
        Directory.CreateDirectory(_cacheRoot);
    }

    public async Task<string> SaveFileAsync(string sourceFilePath, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(sourceFilePath);
        using var streamContent = new StreamContent(fileStream);
        content.Add(streamContent, "file", Path.GetFileName(sourceFilePath));

        var response = await _http.PostAsync("api/files", content, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<UploadFileResponseWire>(ApiJsonOptions.Default, ct)
            ?? throw new InvalidOperationException("Upload response body was empty.");

        // The bytes just uploaded are already sitting right here locally - seed the cache instead of a
        // pointless round-trip re-download the first time GetAbsolutePathAsync is called for this same file
        // (e.g. the operator previewing the presentation they just added).
        var cachedPath = CachePathFor(result.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(cachedPath)!);
        File.Copy(sourceFilePath, cachedPath, overwrite: true);

        return result.RelativePath;
    }

    public async Task DeleteFileAsync(string relativePath, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"api/files/{EscapeRelativePath(relativePath)}", ct);
        response.EnsureSuccessStatusCode();

        var cachedPath = CachePathFor(relativePath);
        if (File.Exists(cachedPath))
        {
            File.Delete(cachedPath);
        }
    }

    public async Task<string> GetAbsolutePathAsync(string relativePath, CancellationToken ct = default)
    {
        var cachedPath = CachePathFor(relativePath);
        if (File.Exists(cachedPath))
        {
            return cachedPath;
        }

        var response = await _http.GetAsync($"api/files/{EscapeRelativePath(relativePath)}", ct);
        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(cachedPath)!);
        await using (var destination = File.Create(cachedPath))
        {
            await response.Content.CopyToAsync(destination, ct);
        }

        return cachedPath;
    }

    private string CachePathFor(string relativePath) =>
        Path.Combine([_cacheRoot, .. relativePath.Split('/', '\\')]);

    private static string EscapeRelativePath(string relativePath) =>
        string.Join('/', relativePath.Split('/', '\\').Select(Uri.EscapeDataString));

    private sealed record UploadFileResponseWire(string RelativePath);
}
