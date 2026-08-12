using PresentationManager.Domain.Entities;

namespace PresentationManager.ApiClient;

/// <summary>Holds the current login's bearer token in memory for this process's lifetime - matches the
/// desktop app's existing behavior of holding the logged-in User in memory with no persistence across
/// restarts (see PresentationManager.UI/Program.cs's LoginForm usage). JwtAuthHandler reads
/// <see cref="Token"/> on every outgoing request and calls <see cref="NotifyExpired"/> when the API
/// responds 401, so a token that expires mid-session (or was revoked) surfaces as one central event instead
/// of every Form having to check for it individually.</summary>
public sealed class AuthSession
{
    public string? Token { get; private set; }

    public User? CurrentUser { get; private set; }

    /// <summary>Raised once, the first time a request comes back 401 after a session was active - subscribe
    /// to force the UI back to LoginForm.</summary>
    public event Action? SessionExpired;

    public void SetSession(string token, User user)
    {
        Token = token;
        CurrentUser = user;
    }

    public void Clear()
    {
        Token = null;
        CurrentUser = null;
    }

    public void NotifyExpired()
    {
        if (Token is null)
        {
            // Already cleared (or never logged in) - the 401 that triggered this was either the login
            // attempt itself failing, or a second in-flight request losing the race after the first one
            // already cleared the session. Either way, only the first one should raise the event.
            return;
        }

        Clear();
        SessionExpired?.Invoke();
    }
}
