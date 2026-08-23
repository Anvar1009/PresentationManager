using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationManager.API.Models;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Enums;
using PresentationManager.TelegramBot;

namespace PresentationManager.API.Controllers.Web;

/// <summary>The Telegram Mini App page a presenter's "📤 Taqdimot yuborish" chat button opens (see
/// <c>PresentationBotHostedService.SendUploadWebAppButtonAsync</c>) - now the ONLY way a presenter submits a
/// presentation: picking the project, typing the title, and uploading the file all happen here, over a plain
/// HTTPS POST, instead of the old in-chat flow (which could never accept a file over 20MB - the Telegram Bot
/// API's own download cap). No login exists for presenters (identities live entirely in Telegram-side tables
/// - see <see cref="Domain.Entities.Presenter"/>), so the single-use... - actually not single-use, see
/// <see cref="PresenterUploadService"/>'s own doc comment - <c>token</c> query value IS the credential here,
/// not a cookie. Deliberately outside every other web surface's shared "_Layout" shell (own minimal,
/// Telegram-themed markup): this only ever renders inside Telegram's in-app browser, never alongside the
/// desktop Judge/Admin/Order panels.</summary>
public sealed class PresenterController : Controller
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ppt", ".pptx", ".pdf" };

    private readonly PresenterUploadService _uploadService;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly ILogger<PresenterController> _logger;

    public PresenterController(PresenterUploadService uploadService, TelegramNotifier telegramNotifier, ILogger<PresenterController> logger)
    {
        _uploadService = uploadService;
        _telegramNotifier = telegramNotifier;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Upload(string token, CancellationToken ct)
    {
        var context = await _uploadService.GetUploadContextAsync(token, ct);
        if (context is null)
        {
            return View("UploadExpired");
        }

        return View(BuildViewModel(token, context));
    }

    [HttpPost]
    [RequestSizeLimit(500_000_000)]
    public async Task<IActionResult> Upload(string token, int projectId, string title, IFormFile? file, CancellationToken ct)
    {
        var context = await _uploadService.GetUploadContextAsync(token, ct);
        if (context is null)
        {
            return View("UploadExpired");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return View(BuildViewModel(token, context) with { SelectedProjectId = projectId, Error = "Sarlavha kiritilishi shart." });
        }

        var extension = file is null ? string.Empty : Path.GetExtension(file.FileName);
        if (file is null || file.Length == 0 || !AllowedExtensions.Contains(extension))
        {
            return View(BuildViewModel(token, context) with
            {
                SelectedProjectId = projectId, Title = title,
                Error = "Fayl formati noto'g'ri. Faqat .ppt, .pptx yoki .pdf qabul qilinadi."
            });
        }

        var fileType = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? PresentationFileType.Pdf : PresentationFileType.Pptx;

        // Written under its own throwaway directory the same way FilesController.Upload does, so
        // PresentationQueueService.AddAsync/UpdateAsync (via IFileStorageService.SaveFileAsync) sees the
        // real original filename rather than an ASP.NET-generated temp name.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var tempPath = Path.Combine(tempDir, Path.GetFileName(file.FileName));
            await using (var tempStream = System.IO.File.Create(tempPath))
            {
                await file.CopyToAsync(tempStream, ct);
            }

            var result = await _uploadService.SubmitAsync(token, projectId, title.Trim(), tempPath, fileType, ct);
            await SendConfirmationAsync(result, fileType, ct);

            return View("UploadSuccess");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Mini-app orqali yuklash rad etildi: {Reason}", ex.Message);
            return View(BuildViewModel(token, context) with { SelectedProjectId = projectId, Title = title, Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mini-app orqali fayl yuklashda xatolik: token {Token}", token);
            return View(BuildViewModel(token, context) with
            {
                SelectedProjectId = projectId, Title = title,
                Error = "Faylni yuklashda kutilmagan xatolik yuz berdi. Qaytadan urinib ko'ring."
            });
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static PresenterUploadViewModel BuildViewModel(string token, PresenterUploadContext context)
    {
        var options = context.Projects.Select(p => new PresenterUploadProjectOption(
            p.ProjectId, p.ProjectName,
            p.SubmissionDeadline is { } d ? d.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : null,
            p.SubmissionDeadline is { } deadline && DateTime.UtcNow > deadline,
            p.ExistingTitle)).ToList();

        return new PresenterUploadViewModel(token, context.FullName, options);
    }

    /// <summary>Mirrors the old in-chat flow's own post-upload confirmation text so a presenter sees the same
    /// message regardless of which upload path (Mini App now, or the retired direct-chat upload before it)
    /// they used.</summary>
    private async Task SendConfirmationAsync(PresenterUploadSubmitResult result, PresentationFileType fileType, CancellationToken ct)
    {
        var fileTypeLabel = fileType == PresentationFileType.Pdf ? "PDF" : "PowerPoint";
        var confirmation =
            (result.IsUpdate ? "✅ Taqdimotingiz yangilandi!\n\n" : "✅ Taqdimotingiz qabul qilindi!\n\n") +
            $"🏛 Loyiha: {result.Project.Name}\n" +
            $"📌 Nomi: {result.Title}\n" +
            $"📄 Fayl turi: {fileTypeLabel}\n\n" +
            $"{EventReminderFormatter.Format(result.Project)}";

        await _telegramNotifier.TrySendMessageAsync(result.ChatId, confirmation, ct: ct);
    }
}
