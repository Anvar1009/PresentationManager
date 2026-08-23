using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationManager.API.Models;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Enums;
using PresentationManager.TelegramBot;

namespace PresentationManager.API.Controllers.Web;

/// <summary>The Telegram Mini App page a presenter's "📤 Faylni brauzerda yuklash" chat button opens (see
/// <c>PresentationBotHostedService.SendUploadWebAppButtonAsync</c>) - lets a file over 20MB reach this server
/// at all, since the Telegram Bot API itself can only ever download what a chat sends it up to that size (see
/// <c>PresentationBotHostedService.HandleDocumentAsync</c>'s own doc comment). No login exists for presenters
/// (identities live entirely in Telegram-side tables - see <see cref="Domain.Entities.Presenter"/>), so the
/// single-use <c>token</c> query value IS the credential here, not a cookie - anyone without it can neither
/// view nor act on this page. Deliberately outside every other web surface's shared "_Layout" shell (own
/// minimal, Telegram-themed markup): this only ever renders inside Telegram's in-app browser, never
/// alongside the desktop Judge/Admin/Order panels.</summary>
public sealed class PresenterController : Controller
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ppt", ".pptx", ".pdf" };

    private readonly PresenterUploadService _uploadService;
    private readonly ProjectService _projectService;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly ILogger<PresenterController> _logger;

    public PresenterController(
        PresenterUploadService uploadService, ProjectService projectService, TelegramNotifier telegramNotifier,
        ILogger<PresenterController> logger)
    {
        _uploadService = uploadService;
        _projectService = projectService;
        _telegramNotifier = telegramNotifier;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Upload(string token, CancellationToken ct)
    {
        var record = await _uploadService.GetValidTokenAsync(token, ct);
        if (record is null)
        {
            return View("UploadExpired");
        }

        return View(new PresenterUploadViewModel(token, record.ProjectName, record.Title, record.ExistingPresentationId is not null));
    }

    [HttpPost]
    [RequestSizeLimit(500_000_000)]
    public async Task<IActionResult> Upload(string token, IFormFile? file, CancellationToken ct)
    {
        var record = await _uploadService.GetValidTokenAsync(token, ct);
        if (record is null)
        {
            return View("UploadExpired");
        }

        var model = new PresenterUploadViewModel(token, record.ProjectName, record.Title, record.ExistingPresentationId is not null);

        var extension = file is null ? string.Empty : Path.GetExtension(file.FileName);
        if (file is null || file.Length == 0 || !AllowedExtensions.Contains(extension))
        {
            return View(model with { Error = "Fayl formati noto'g'ri. Faqat .ppt, .pptx yoki .pdf qabul qilinadi." });
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

            await _uploadService.SubmitAsync(token, tempPath, fileType, ct);
            await SendConfirmationAsync(record.ChatId, record.ProjectId, record.ProjectName, record.Title,
                fileType, record.ExistingPresentationId is not null, ct);

            return View("UploadSuccess");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Mini-app orqali yuklash rad etildi: {Reason}", ex.Message);
            return View(model with { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mini-app orqali fayl yuklashda xatolik: chat {ChatId}", record.ChatId);
            return View(model with { Error = "Faylni yuklashda kutilmagan xatolik yuz berdi. Qaytadan urinib ko'ring." });
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>Mirrors <c>PresentationBotHostedService.HandleDocumentAsync</c>'s own post-upload confirmation
    /// text so a presenter sees the same message regardless of which upload path they used.</summary>
    private async Task SendConfirmationAsync(
        long chatId, int projectId, string projectName, string title, PresentationFileType fileType, bool isUpdate, CancellationToken ct)
    {
        var fileTypeLabel = fileType == PresentationFileType.Pdf ? "PDF" : "PowerPoint";
        var confirmation =
            (isUpdate ? "✅ Taqdimotingiz yangilandi!\n\n" : "✅ Taqdimotingiz qabul qilindi!\n\n") +
            $"🏛 Loyiha: {projectName}\n" +
            $"📌 Nomi: {title}\n" +
            $"📄 Fayl turi: {fileTypeLabel}";

        var projects = await _projectService.GetAllAsync(ct);
        var project = projects.FirstOrDefault(p => p.Id == projectId);
        if (project is not null)
        {
            confirmation += $"\n\n{EventReminderFormatter.Format(project)}";
        }

        await _telegramNotifier.TrySendMessageAsync(chatId, confirmation, ct: ct);
    }
}
