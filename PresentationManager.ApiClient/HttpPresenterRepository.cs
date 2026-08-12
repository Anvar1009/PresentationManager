using System.Net;
using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient;

public sealed class HttpPresenterRepository : IPresenterRepository
{
    private readonly HttpClient _http;

    public HttpPresenterRepository(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Presenter>> GetAllAsync(CancellationToken ct = default)
    {
        var wires = await _http.GetFromJsonAsync<List<PresenterWire>>("api/presenters", ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }

    public async Task<Presenter?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await GetOneAsync($"api/presenters/{id}", ct);

    public async Task<Presenter?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default) =>
        await GetOneAsync($"api/presenters/by-telegram/{telegramChatId}", ct);

    public async Task<Presenter> AddAsync(Presenter presenter, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/presenters", PresenterWire.FromEntity(presenter), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<PresenterWire>(ApiJsonOptions.Default, ct)
            ?? throw new InvalidOperationException("Create presenter response body was empty.");
        return wire.ToEntity();
    }

    private async Task<Presenter?> GetOneAsync(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<PresenterWire>(ApiJsonOptions.Default, ct);
        return wire?.ToEntity();
    }
}
