using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient;

public sealed class HttpSettingsRepository : ISettingsRepository
{
    private readonly HttpClient _http;

    public HttpSettingsRepository(HttpClient http)
    {
        _http = http;
    }

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        var wire = await _http.GetFromJsonAsync<SettingsWire>("api/settings", ApiJsonOptions.Default, ct)
            ?? throw new InvalidOperationException("Settings response body was empty.");
        return wire.ToEntity();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync("api/settings", SettingsWire.FromEntity(settings), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }
}
