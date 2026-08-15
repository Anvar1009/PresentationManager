using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient;

public sealed class HttpPresenterProjectAssignmentRepository : IPresenterProjectAssignmentRepository
{
    private readonly HttpClient _http;

    public HttpPresenterProjectAssignmentRepository(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<PresenterProjectAssignment>> GetByProjectIdAsync(int projectId, CancellationToken ct = default) =>
        await GetListAsync($"api/presenter-assignments/project/{projectId}", ct);

    public async Task<List<PresenterProjectAssignment>> GetByPresenterIdAsync(int presenterId, CancellationToken ct = default) =>
        await GetListAsync($"api/presenter-assignments/presenter/{presenterId}", ct);

    public async Task<bool> ExistsAsync(int projectId, int presenterId, CancellationToken ct = default)
    {
        var assignments = await GetByProjectIdAsync(projectId, ct);
        return assignments.Any(a => a.PresenterId == presenterId);
    }

    public async Task<PresenterProjectAssignment> AddAsync(PresenterProjectAssignment assignment, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/presenter-assignments", PresenterProjectAssignmentWire.FromEntity(assignment), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<PresenterProjectAssignmentWire>(ApiJsonOptions.Default, ct)
            ?? throw new InvalidOperationException("Create presenter assignment response body was empty.");
        return wire.ToEntity();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"api/presenter-assignments/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<PresenterProjectAssignment>> GetListAsync(string url, CancellationToken ct)
    {
        var wires = await _http.GetFromJsonAsync<List<PresenterProjectAssignmentWire>>(url, ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }
}
