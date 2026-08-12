using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.API.Controllers;

/// <summary>The only way PresentationManager.UI still sends a Telegram message (password reset codes,
/// SuperAdmin-issued credentials) - see ITelegramSender/HttpTelegramSender. The bot token this relays
/// through never leaves this process.</summary>
[ApiController]
[Authorize]
[Route("api/notifications/telegram")]
public sealed class NotificationsController : ControllerBase
{
    private readonly ITelegramSender _telegramSender;

    public NotificationsController(ITelegramSender telegramSender)
    {
        _telegramSender = telegramSender;
    }

    [HttpPost("send")]
    public async Task<ActionResult<bool>> Send(SendTelegramMessageRequest request, CancellationToken ct) =>
        Ok(await _telegramSender.TrySendMessageAsync(request.ChatId, request.Text, ct));
}
