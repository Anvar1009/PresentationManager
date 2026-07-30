namespace PresentationManager.Application.Services;

/// <summary>One presenter who has submitted at least one presentation to a project — the Admin panel's
/// "Qatnashchilar" table row.</summary>
public sealed class ProjectParticipant
{
    public required string FullName { get; init; }

    public string? PhoneNumber { get; init; }

    public required int PresentationCount { get; init; }
}
