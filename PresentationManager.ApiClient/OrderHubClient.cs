using Microsoft.AspNetCore.SignalR.Client;

namespace PresentationManager.ApiClient;

/// <summary>Thin wrapper around a <see cref="HubConnection"/> to PresentationManager.API's
/// PresentationOrderHub - lets AdminForm learn the instant a "Tartib operatori" randomizes a project's
/// presentation order, instead of waiting for its own 5s background poll. Registered as a DI singleton but
/// only actually connects once <see cref="ConnectAsync"/> is called (from AdminForm, after login) - built at
/// construction time it would have no bearer token yet, since every role's dashboard form is itself a DI
/// singleton resolved before <see cref="AuthSession.Token"/> is ever set (see PresentationManager.UI\Program.cs).</summary>
public sealed class OrderHubClient : IAsyncDisposable
{
    private readonly string _apiBaseUrl;
    private readonly AuthSession _authSession;
    private HubConnection? _connection;

    public OrderHubClient(string apiBaseUrl, AuthSession authSession)
    {
        _apiBaseUrl = apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/";
        _authSession = authSession;
    }

    /// <summary>Payload is the Id of the project whose order changed - callers ignore it if it's not the
    /// project they're currently showing.</summary>
    public event Action<int>? OrderRandomized;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{_apiBaseUrl}hubs/presentation-order", options =>
            {
                options.Headers.Add("Authorization", $"Bearer {_authSession.Token}");
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<int>("OrderRandomized", projectId => OrderRandomized?.Invoke(projectId));

        await _connection.StartAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
