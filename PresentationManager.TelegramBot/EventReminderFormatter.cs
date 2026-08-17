using PresentationManager.Domain.Entities;

namespace PresentationManager.TelegramBot;

/// <summary>Formats a project's date/time/location into the reminder line appended to any Telegram message
/// telling a presenter "you're now part of this project" - shared between
/// <see cref="PresentationBotHostedService"/> (post-upload confirmation) and the server-side notify blocks in
/// PresentationManager.API's <c>PresenterAssignmentsController.Add</c>/<c>Controllers.Web.AdminController.AssignPresenter</c>
/// (post-assignment confirmation), so both say the same thing rather than drifting apart.</summary>
public static class EventReminderFormatter
{
    public static string Format(Project project)
    {
        var dateText = project.EventStartDate == project.EventEndDate
            ? project.EventStartDate.ToString("dd.MM.yyyy")
            : $"{project.EventStartDate:dd.MM.yyyy} - {project.EventEndDate:dd.MM.yyyy}";

        var lines = new List<string> { $"📅 Sana: {dateText}" };
        if (project.EventTime is { } time)
        {
            lines.Add($"🕒 Vaqti: {time:HH:mm}");
        }
        if (!string.IsNullOrWhiteSpace(project.Location))
        {
            lines.Add($"📍 Manzil: {project.Location}");
        }

        return string.Join('\n', lines);
    }
}
