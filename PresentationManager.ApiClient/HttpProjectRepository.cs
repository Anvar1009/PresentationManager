using System.Net;
using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient;

public sealed class HttpProjectRepository : IProjectRepository
{
    private readonly HttpClient _http;

    public HttpProjectRepository(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Project>> GetAllAsync(CancellationToken ct = default)
    {
        var wires = await _http.GetFromJsonAsync<List<ProjectWire>>("api/projects", ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }

    public async Task<List<Project>> GetByCreatorAsync(int createdByUserId, CancellationToken ct = default)
    {
        var wires = await _http.GetFromJsonAsync<List<ProjectWire>>($"api/projects?createdBy={createdByUserId}", ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }

    public async Task<Project?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/projects/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<ProjectWire>(ApiJsonOptions.Default, ct);
        return wire?.ToEntity();
    }

    public async Task<Project> AddAsync(Project project, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/projects", CreateProjectWireRequest.FromEntity(project), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<ProjectWire>(ApiJsonOptions.Default, ct)
            ?? throw new InvalidOperationException("Create project response body was empty.");
        return wire.ToEntity();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"api/projects/{id}", ct);
        response.EnsureSuccessStatusCode();
    }
}
