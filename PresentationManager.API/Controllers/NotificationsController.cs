using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(ITelegramSender telegramSender, ILogger<NotificationsController> logger)
    {
        _telegramSender = telegramSender;
        _logger = logger;
    }

    [HttpPost("send")]
    public async Task<ActionResult<bool>> Send(SendTelegramMessageRequest request, CancellationToken ct)
    {
        var sent = await _telegramSender.TrySendMessageAsync(request.ChatId, request.Text, ct);
        if (sent)
        {
            _logger.LogInformation("Telegram xabari yuborildi: chat {ChatId}", request.ChatId);
        }
        else
        {
            _logger.LogWarning("Telegram xabarini yuborib bo'lmadi: chat {ChatId}", request.ChatId);
        }

        return Ok(sent);
    }
}
