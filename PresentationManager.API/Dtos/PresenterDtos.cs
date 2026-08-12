using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Dtos;

public sealed record PresenterDto(
    int Id,
    long TelegramChatId,
    string FullName,
    string? PhoneNumber,
    string? TelegramUsername,
    DateTime CreatedAt)
{
    public static PresenterDto FromEntity(Presenter p) => new(
        p.Id, p.TelegramChatId, p.FullName, p.PhoneNumber, p.TelegramUsername, p.CreatedAt);

    public Presenter ToEntity() => new()
    {
        Id = Id,
        TelegramChatId = TelegramChatId,
        FullName = FullName,
        PhoneNumber = PhoneNumber,
        TelegramUsername = TelegramUsername,
        CreatedAt = CreatedAt
    };
}
