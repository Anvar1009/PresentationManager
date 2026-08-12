using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient.Wire;

/// <summary>Mirrors PresentationManager.API.Dtos.PresenterDto.</summary>
internal sealed record PresenterWire(
    int Id, long TelegramChatId, string FullName, string? PhoneNumber, string? TelegramUsername, DateTime CreatedAt)
{
    public static PresenterWire FromEntity(Presenter p) => new(
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
