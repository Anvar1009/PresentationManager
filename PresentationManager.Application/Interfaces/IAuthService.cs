using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

/// <summary>The one deliberate exception to "every interface just gets an HTTP-backed implementation
/// swapped in" - a password can never be verified client-side against a hash it was never given, so login
/// needs its own contract rather than reusing IUserRepository. PresentationManager.API's AuthController is
/// the only place a password is ever compared; PresentationManager.ApiClient's HttpAuthService is the only
/// implementation that matters in practice (PresentationManager.UI has nothing else to run this against
/// now that it no longer touches the database directly).</summary>
public interface IAuthService
{
    /// <summary>Null on bad credentials or a disabled account - mirrors UserService.ValidateLoginAsync's
    /// existing null-on-failure contract so LoginForm's calling code barely changes.</summary>
    Task<AuthResult?> LoginAsync(string username, string password, CancellationToken ct = default);
}

public sealed record AuthResult(User User, string Token);
