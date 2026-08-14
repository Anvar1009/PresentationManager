namespace PresentationManager.API.Models;

public sealed record OrderProjectOption(int ProjectId, string ProjectName);

public sealed record OrderDashboardViewModel(string OrderOperatorFullName, IReadOnlyList<OrderProjectOption> Projects);

/// <summary>One project's presenter names, in their current order - see OrderController.Project/.Randomize.</summary>
public sealed record OrderProjectViewModel(int ProjectId, string ProjectName, IReadOnlyList<string> PresenterNames);
