using System.Net.Http.Json;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.ApiClient;

public sealed class HttpTelegramSender : ITelegramSender
{
    private readonly HttpClient _http;

    public HttpTelegramSender(HttpClient http)
    {
        _http = http;
    }

    public async Task<bool> TrySendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "api/notifications/telegram/send", new { ChatId = chatId, Text = text }, ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<bool>(ApiJsonOptions.Default, ct);
    }
}
