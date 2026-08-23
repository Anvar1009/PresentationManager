namespace PresentationManager.API.Models;

/// <summary>Backs the Telegram Mini App upload page (Controllers.Web.PresenterController.Upload) - everything
/// the bot conversation already gathered, echoed back so the presenter can confirm it's the right project/
/// title before picking a file, plus whatever went wrong on the last attempt (if any).</summary>
public sealed record PresenterUploadViewModel(
    string Token, string ProjectName, string Title, bool IsUpdate, string? Error = null);
