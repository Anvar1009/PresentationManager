using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PresentationManager.API.Hubs;

/// <summary>Server-push only - broadcasts "OrderRandomized" (see <see cref="Controllers.PresentationsController.RandomizeOrder"/>)
/// to every connected client the moment a "Tartib operatori" shuffles a project's presentation order, so
/// AdminForm's queue view (JWT-authenticated WinForms client) and the Judge web platform's project page
/// (Cookie-authenticated browser client) both update live instead of waiting for a poll. Both schemes are
/// listed explicitly - this API's default scheme is JwtBearer, which a browser's cookie-only request would
/// otherwise fail. No client-invocable methods; clients only ever listen.</summary>
[Authorize(AuthenticationSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{CookieAuthenticationDefaults.AuthenticationScheme}")]
public sealed class PresentationOrderHub : Hub;
