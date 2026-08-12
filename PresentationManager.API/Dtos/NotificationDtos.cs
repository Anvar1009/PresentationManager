namespace PresentationManager.API.Dtos;

public sealed record SendTelegramMessageRequest(long ChatId, string Text);
