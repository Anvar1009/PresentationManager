using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PresentationManager.API.Hubs;

/// <summary>Server-push only - broadcasts "OrderRandomized" (see <see cref="Controllers.PresentationsController.RandomizeOrder"/>)
/// to every connected client the moment a "Tartib operatori" shuffles a project's presentation order, so
/// AdminForm's queue view updates live instead of waiting for its next background poll. No client-invocable
/// methods; clients only ever listen.</summary>
[Authorize]
public sealed class PresentationOrderHub : Hub;
