namespace PresentationManager.API.Models;

/// <summary>One project the presenter can submit to, as offered on the upload page - <see cref="IsExpired"/>
/// disables selecting it (the server re-checks the deadline itself regardless - see
/// <see cref="Application.Services.PresenterUploadService.SubmitAsync"/> - this is only so the page doesn't
/// let someone fill out a form it already knows will be rejected).</summary>
public sealed record PresenterUploadProjectOption(
    int ProjectId, string ProjectName, string? DeadlineText, bool IsExpired, string? ExistingTitle);

/// <summary>Backs the Telegram Mini App upload page (Controllers.Web.PresenterController.Upload) - the
/// presenter's name, every project they can currently submit to, and (on redisplay after a rejected POST)
/// whatever they'd already picked plus why it was rejected.</summary>
public sealed record PresenterUploadViewModel(
    string Token,
    string FullName,
    IReadOnlyList<PresenterUploadProjectOption> Projects,
    int? SelectedProjectId = null,
    string? Title = null,
    string? Error = null);
