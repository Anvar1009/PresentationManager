using System.Net;
using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient;

public sealed class HttpPresentationRepository : IPresentationRepository
{
    private readonly HttpClient _http;

    public HttpPresentationRepository(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Presentation>> GetAllAsync(CancellationToken ct = default) =>
        await GetListAsync("api/presentations", ct);

    public async Task<List<Presentation>> GetAllOrderedAsync(int projectId, CancellationToken ct = default) =>
        await GetListAsync($"api/presentations/project/{projectId}/ordered", ct);

    public async Task<List<Presentation>> GetByProjectIdAsync(int projectId, CancellationToken ct = default) =>
        await GetListAsync($"api/presentations/project/{projectId}", ct);

    public async Task<List<Presentation>> SearchByNameAsync(string query, int projectId, CancellationToken ct = default) =>
        await GetListAsync($"api/presentations/project/{projectId}/search?q={Uri.EscapeDataString(query)}", ct);

    public async Task<Presentation?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/presentations/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<PresentationWire>(ApiJsonOptions.Default, ct);
        return wire?.ToEntity();
    }

    public async Task<Presentation> AddAsync(Presentation presentation, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/presentations", PresentationWire.FromEntity(presentation), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<PresentationWire>(ApiJsonOptions.Default, ct)
            ?? throw new InvalidOperationException("Create presentation response body was empty.");
        return wire.ToEntity();
    }

    public async Task UpdateAsync(Presentation presentation, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"api/presentations/{presentation.Id}", PresentationWire.FromEntity(presentation), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"api/presentations/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReorderAsync(IReadOnlyList<int> orderedPresentationIds, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "api/presentations/reorder",
            new { OrderedPresentationIds = orderedPresentationIds },
            ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<Presentation>> GetListAsync(string url, CancellationToken ct)
    {
        var wires = await _http.GetFromJsonAsync<List<PresentationWire>>(url, ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }
}
