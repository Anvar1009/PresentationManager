using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient;

public sealed class HttpHistoryRepository : IHistoryRepository
{
    private readonly HttpClient _http;

    public HttpHistoryRepository(HttpClient http)
    {
        _http = http;
    }

    public async Task AddAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/history", CreateHistoryEntryWireRequest.FromEntity(entry), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<HistoryEntry>> GetRecentAsync(int count = 200, CancellationToken ct = default)
    {
        var wires = await _http.GetFromJsonAsync<List<HistoryEntryWire>>($"api/history?count={count}", ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }

    public async Task<List<HistoryEntry>> GetForPresentationAsync(int presentationId, CancellationToken ct = default)
    {
        var wires = await _http.GetFromJsonAsync<List<HistoryEntryWire>>($"api/history/presentation/{presentationId}", ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync("api/history", ct);
        response.EnsureSuccessStatusCode();
    }
}
