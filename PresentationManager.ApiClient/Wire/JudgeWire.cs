using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient.Wire;

/// <summary>Mirrors PresentationManager.API.Dtos.JudgeDto.</summary>
internal sealed record JudgeWire(int Id, int ProjectId, string PhoneNumber, string? FullName, long? TelegramChatId, DateTime CreatedAt)
{
    public static JudgeWire FromEntity(Judge j) => new(j.Id, j.ProjectId, j.PhoneNumber, j.FullName, j.TelegramChatId, j.CreatedAt);

    public Judge ToEntity() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        PhoneNumber = PhoneNumber,
        FullName = FullName,
        TelegramChatId = TelegramChatId,
        CreatedAt = CreatedAt
    };
}
