namespace PresentationManager.API.Models;

public sealed record OrderProjectOption(int ProjectId, string ProjectName);

public sealed record OrderDashboardViewModel(string OrderOperatorFullName, IReadOnlyList<OrderProjectOption> Projects);
