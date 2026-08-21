using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.API.Controllers;

/// <summary>Byte-streaming upload/download for presentation files, backed by the same
/// <see cref="IFileStorageService"/> (the real local-disk PresentationManager.Infrastructure.Services.
/// FileStorageService) that PresentationManager.BotService already uses for its own uploads - both
/// processes' "Storage:RootPath" now point at the same local folder on this server, no shared network
/// path needed. PresentationManager.UI's HttpFileStorageService is the only client: it uploads a locally
/// picked file here and downloads+caches bytes locally before handing PowerPoint COM automation a real
/// file path (COM can only ever open a local file, never a URL).</summary>
[ApiController]
[Authorize]
[Route("api/files")]
public sealed class FilesController : ControllerBase
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<FilesController> _logger;

    public FilesController(IFileStorageService fileStorageService, ILogger<FilesController> logger)
    {
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(500_000_000)]
    public async Task<ActionResult<UploadFileResponse>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            _logger.LogWarning("Bo'sh fayl bilan yuklashga urinish.");
            return BadRequest("Fayl bo'sh.");
        }

        // Written under its own throwaway directory (not just a temp file) so the original filename
        // survives into SaveFileAsync's stored name - see FileStorageService.SaveFileAsync, which derives
        // the stored name from Path.GetFileNameWithoutExtension(sourceFilePath). Path.GetFileName strips any
        // directory component a hostile client might smuggle into IFormFile.FileName before it ever reaches
        // Path.Combine.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var tempPath = Path.Combine(tempDir, Path.GetFileName(file.FileName));
            await using (var tempStream = System.IO.File.Create(tempPath))
            {
                await file.CopyToAsync(tempStream, ct);
            }

            var relativePath = await _fileStorageService.SaveFileAsync(tempPath, ct);
            _logger.LogInformation("Fayl API orqali yuklandi: {RelativePath} ({Size} bayt)", relativePath, file.Length);
            return Ok(new UploadFileResponse(relativePath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Faylni yuklashda xatolik: {FileName}", file.FileName);
            throw;
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [HttpGet("{*relativePath}")]
    public async Task<IActionResult> Download(string relativePath, CancellationToken ct)
    {
        if (!IsSafeRelativePath(relativePath))
        {
            _logger.LogWarning("Xavfsiz bo'lmagan fayl yo'li bilan yuklab olishga urinish: {RelativePath}", relativePath);
            return BadRequest();
        }

        var absolutePath = await _fileStorageService.GetAbsolutePathAsync(relativePath, ct);
        if (!System.IO.File.Exists(absolutePath))
        {
            _logger.LogWarning("Yuklab olish uchun fayl topilmadi: {RelativePath}", relativePath);
            return NotFound();
        }

        return PhysicalFile(absolutePath, "application/octet-stream", Path.GetFileName(absolutePath));
    }

    [HttpDelete("{*relativePath}")]
    public async Task<IActionResult> Delete(string relativePath, CancellationToken ct)
    {
        if (!IsSafeRelativePath(relativePath))
        {
            _logger.LogWarning("Xavfsiz bo'lmagan fayl yo'li bilan o'chirishga urinish: {RelativePath}", relativePath);
            return BadRequest();
        }

        await _fileStorageService.DeleteFileAsync(relativePath, ct);
        _logger.LogInformation("Fayl API orqali o'chirildi: {RelativePath}", relativePath);
        return NoContent();
    }

    /// <summary>Rejects anything that could escape the storage root once resolved - relativePath comes
    /// straight from the URL, unlike every other caller of IFileStorageService today, which only ever
    /// passed a path this same process generated itself (see FileStorageService.SaveFileAsync's
    /// "{guid}_{name}.ext" naming).</summary>
    private static bool IsSafeRelativePath(string relativePath) =>
        !string.IsNullOrWhiteSpace(relativePath)
        && !Path.IsPathRooted(relativePath)
        && !relativePath.Split('/', '\\').Contains("..");
}
