using System.Net;
using System.Net.Http.Headers;

namespace PresentationManager.ApiClient;

/// <summary>Attaches the current bearer token (if any) to every outgoing request, and clears the session
/// the moment the API responds 401 - registered on every typed HttpClient in this project (see
/// PresentationManager.UI/Program.cs's AddHttpClient calls) so no individual Http*Repository class has to
/// deal with auth itself.</summary>
public sealed class JwtAuthHandler : DelegatingHandler
{
    private readonly AuthSession _session;

    public JwtAuthHandler(AuthSession session)
    {
        _session = session;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_session.Token is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _session.NotifyExpired();
        }

        return response;
    }
}
