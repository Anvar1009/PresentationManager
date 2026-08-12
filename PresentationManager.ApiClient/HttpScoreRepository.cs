using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient;

public sealed class HttpScoreRepository : IScoreRepository
{
    private readonly HttpClient _http;

    public HttpScoreRepository(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Score>> GetAllAsync(CancellationToken ct = default) =>
        await GetListAsync("api/scores", ct);

    public async Task<List<Score>> GetByPresentationAndJudgeAsync(int presentationId, int judgeId, CancellationToken ct = default) =>
        await GetListAsync($"api/scores/presentation/{presentationId}/judge/{judgeId}", ct);

    public async Task<List<Score>> GetByPresentationIdsAsync(IReadOnlyList<int> presentationIds, CancellationToken ct = default) =>
        await GetListAsync($"api/scores/by-presentations?ids={string.Join(',', presentationIds)}", ct);

    public async Task UpsertAsync(int presentationId, int judgeId, int criterionId, int value, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "api/scores", new UpsertScoreWireRequest(presentationId, judgeId, criterionId, value), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<Score>> GetListAsync(string url, CancellationToken ct)
    {
        var wires = await _http.GetFromJsonAsync<List<ScoreWire>>(url, ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }
}
