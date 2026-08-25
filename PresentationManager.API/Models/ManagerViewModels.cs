using System.ComponentModel.DataAnnotations;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Models;

/// <summary>Drives the shared <c>_ManagerTabs</c> partial's active-tab highlight - one of "dashboard"/
/// "projects"/"presentations"/"presenters"/"judges"/"users"/"scores"/"jurnal".</summary>
public sealed record ManagerTabsViewModel(string Active);

/// <summary>Org-scoped counterpart to <see cref="SuperAdminDashboardViewModel"/> - same shape, minus the
/// organization-wide totals SuperAdmin sees, plus <see cref="OrganizationName"/> so the page reads as "this
/// tenant's" numbers rather than the whole system's.</summary>
public sealed record ManagerDashboardViewModel(
    string ManagerFullName,
    string OrganizationName,
    int ProjectCount,
    int PresenterCount,
    int UserCount,
    int JudgeCount,
    int PresentationCount,
    IReadOnlyList<SuperAdminStatusCount> PresentationsByStatus,
    IReadOnlyList<SuperAdminTopProjectRow> TopProjects,
    IReadOnlyList<SuperAdminActivityRow> RecentActivity);

/// <summary>Reuses <see cref="Project"/> as-is, same as <see cref="SuperAdminProjectsViewModel"/> - already
/// pre-filtered to this Manager's own organization by the controller.</summary>
public sealed record ManagerProjectsViewModel(string? Query, IReadOnlyList<Project> Projects);

/// <summary>Reuses <see cref="Presenter"/> as-is, same as <see cref="SuperAdminPresentersViewModel"/> -
/// already pre-filtered to presenters assigned to this Manager's own organization's projects.</summary>
public sealed record ManagerPresentersViewModel(string? Query, IReadOnlyList<Presenter> Presenters);

/// <summary>Only Admin/Operator accounts ever show here - a Manager's own tenant's staff, never other
/// Managers/SuperAdmin (see <see cref="ManagerCreateUserViewModel.Role"/>'s own doc comment).</summary>
public sealed record ManagerUsersViewModel(string? Query, IReadOnlyList<User> Users);

/// <summary>Manager's own "+ Foydalanuvchi qo'shish" form - narrower than SuperAdmin's
/// <see cref="CreateUserViewModel"/>: no organization picker (always this Manager's own, stamped server-side)
/// and the role dropdown offers only <see cref="UserRole.Admin"/>/<see cref="UserRole.Operator"/> - enforced
/// again server-side in <c>ManagerController.CreateUser</c>'s POST handler, not just by what the dropdown
/// renders.</summary>
public sealed class ManagerCreateUserViewModel
{
    [Required(ErrorMessage = "Login kiritilishi shart.")]
    [Display(Name = "Login")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parol kiritilishi shart.")]
    [Display(Name = "Parol")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ism-familiya kiritilishi shart.")]
    [Display(Name = "Ism-familiya")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Rol")]
    public UserRole Role { get; set; } = UserRole.Operator;
}

/// <summary>Manager's own "Login/parolni tiklash" form - same restriction as
/// <see cref="ManagerCreateUserViewModel.Role"/>.</summary>
public sealed class ManagerEditUserViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Login kiritilishi shart.")]
    [Display(Name = "Login")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ism-familiya kiritilishi shart.")]
    [Display(Name = "Ism-familiya")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Yangi parol")]
    public string? NewPassword { get; set; }

    [Display(Name = "Rol")]
    public UserRole Role { get; set; }
}
