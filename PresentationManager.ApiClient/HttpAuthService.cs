using System.Net;
using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.ApiClient;

public sealed class HttpAuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly AuthSession _session;

    public HttpAuthService(HttpClient http, AuthSession session)
    {
        _http = http;
        _session = session;
    }

    public async Task<AuthResult?> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new { username, password }, ApiJsonOptions.Default, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LoginResponseWire>(ApiJsonOptions.Default, ct)
            ?? throw new InvalidOperationException("Login response body was empty.");

        var user = payload.User.ToEntity();
        _session.SetSession(payload.Token, user);
        return new AuthResult(user, payload.Token);
    }

    private sealed record LoginResponseWire(string Token, UserWire User);
}
