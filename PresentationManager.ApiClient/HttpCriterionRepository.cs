using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient;

public sealed class HttpCriterionRepository : ICriterionRepository
{
    private readonly HttpClient _http;

    public HttpCriterionRepository(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<EvaluationCriterion>> GetAllAsync(CancellationToken ct = default)
    {
        var wires = await _http.GetFromJsonAsync<List<CriterionWire>>("api/criteria", ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }

    public async Task<List<EvaluationCriterion>> GetByProjectIdAsync(int projectId, CancellationToken ct = default)
    {
        var wires = await _http.GetFromJsonAsync<List<CriterionWire>>($"api/criteria/project/{projectId}", ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }

    public async Task<EvaluationCriterion> AddAsync(EvaluationCriterion criterion, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/criteria", CriterionWire.FromEntity(criterion), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<CriterionWire>(ApiJsonOptions.Default, ct)
            ?? throw new InvalidOperationException("Create criterion response body was empty.");
        return wire.ToEntity();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"api/criteria/{id}", ct);
        response.EnsureSuccessStatusCode();
    }
}
