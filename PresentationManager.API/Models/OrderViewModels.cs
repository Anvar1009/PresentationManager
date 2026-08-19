namespace PresentationManager.API.Models;

/// <summary><paramref name="IsOrdered"/> drives the "Tartiblangan" badge on the Order dashboard - see
/// <see cref="PresentationManager.Domain.Entities.Project.OrderRandomizedAt"/>.</summary>
public sealed record OrderProjectOption(int ProjectId, string ProjectName, int PresentationCount, bool IsOrdered);

public sealed record OrderDashboardViewModel(string OrderOperatorFullName, IReadOnlyList<OrderProjectOption> Projects);

/// <summary>One project's presenter names, in their current order - see OrderController.Project/.Randomize.</summary>
public sealed record OrderProjectViewModel(int ProjectId, string ProjectName, IReadOnlyList<string> PresenterNames);
