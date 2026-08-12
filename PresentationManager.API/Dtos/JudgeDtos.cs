using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Dtos;

public sealed record JudgeDto(int Id, int ProjectId, string PhoneNumber, string? FullName, long? TelegramChatId, DateTime CreatedAt)
{
    public static JudgeDto FromEntity(Judge j) => new(j.Id, j.ProjectId, j.PhoneNumber, j.FullName, j.TelegramChatId, j.CreatedAt);

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
