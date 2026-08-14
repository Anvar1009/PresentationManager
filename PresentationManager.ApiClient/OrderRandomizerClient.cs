namespace PresentationManager.ApiClient;

/// <summary>"Tartib operatori" role's one action - calls the SuperAdmin/OrderOperator-only randomize-order
/// endpoint (PresentationManager.API.Controllers.PresentationsController.RandomizeOrder). Not part of
/// <see cref="Application.Interfaces.IPresentationRepository"/> - that interface is a symmetric Infrastructure/
/// ApiClient contract, and this compound action (shuffle + SignalR broadcast) has no Infrastructure-side
/// twin to keep in sync with.</summary>
public sealed class OrderRandomizerClient
{
    private readonly HttpClient _http;

    public OrderRandomizerClient(HttpClient http)
    {
        _http = http;
    }

    public async Task RandomizeOrderAsync(int projectId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"api/presentations/project/{projectId}/randomize-order", null, ct);
        response.EnsureSuccessStatusCode();
    }
}
