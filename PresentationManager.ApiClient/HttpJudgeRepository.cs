using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient;

public sealed class HttpJudgeRepository : IJudgeRepository
{
    private readonly HttpClient _http;

    public HttpJudgeRepository(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Judge>> GetAllAsync(CancellationToken ct = default) =>
        await GetListAsync("api/judges", ct);

    public async Task<List<Judge>> GetByProjectIdAsync(int projectId, CancellationToken ct = default) =>
        await GetListAsync($"api/judges/project/{projectId}", ct);

    public async Task<List<Judge>> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default) =>
        await GetListAsync($"api/judges/by-telegram/{telegramChatId}", ct);

    public async Task<Judge> AddAsync(Judge judge, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/judges", JudgeWire.FromEntity(judge), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<JudgeWire>(ApiJsonOptions.Default, ct)
            ?? throw new InvalidOperationException("Create judge response body was empty.");
        return wire.ToEntity();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"api/judges/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<Judge>> GetListAsync(string url, CancellationToken ct)
    {
        var wires = await _http.GetFromJsonAsync<List<JudgeWire>>(url, ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }
}
