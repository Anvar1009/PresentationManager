using System.ComponentModel.DataAnnotations;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Models;

public sealed record SuperAdminStatusCount(string Label, int Count);

/// <summary>Drives the shared <c>_SuperAdminTabs</c> partial's active-tab highlight - one of "dashboard"/
/// "projects"/"presentations"/"presenters"/"judges"/"users"/"scores"/"jurnal".</summary>
public sealed record SuperAdminTabsViewModel(string Active);

public sealed record SuperAdminDashboardViewModel(
    string SuperAdminFullName,
    int ProjectCount,
    int PresenterCount,
    int UserCount,
    int JudgeCount,
    int PresentationCount,
    IReadOnlyList<SuperAdminStatusCount> PresentationsByStatus);

/// <summary>Reuses <see cref="Project"/> as-is - the SuperAdmin panel's "Loyihalar" tab is read-only, so no
/// reshaping is needed beyond what the entity already carries.</summary>
public sealed record SuperAdminProjectsViewModel(string? Query, IReadOnlyList<Project> Projects);

public sealed record SuperAdminPresentationRow(
    int PresentationId, string ProjectName, string PresenterFullName, string Title, string StatusLabel, string CreatedAt);

public sealed record SuperAdminPresentationsViewModel(string? Query, IReadOnlyList<SuperAdminPresentationRow> Presentations);

/// <summary>Reuses <see cref="Presenter"/> as-is - read-only "Taqdimotchilar" tab.</summary>
public sealed record SuperAdminPresentersViewModel(string? Query, IReadOnlyList<Presenter> Presenters);

public sealed record SuperAdminJudgeRow(int Id, string ProjectName, string FullName, string PhoneNumber);

public sealed record SuperAdminJudgesViewModel(string? Query, IReadOnlyList<SuperAdminJudgeRow> Judges);

/// <summary>Reuses <see cref="User"/> as-is - the one section with real CRUD (create/edit/reset-password/
/// change-role), all handled by dedicated actions/forms rather than reshaping the list row itself.</summary>
public sealed record SuperAdminUsersViewModel(string? Query, IReadOnlyList<User> Users);

/// <summary>SuperAdmin panel's "+ Foydalanuvchi qo'shish" form - mirrors <c>AddUserForm</c>.</summary>
public sealed class CreateUserViewModel
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

/// <summary>SuperAdmin panel's "Login/parolni tiklash" form - mirrors <c>EditUserForm</c>.
/// <see cref="NewPassword"/> left blank keeps the current password unchanged.</summary>
public sealed class EditUserViewModel
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

public sealed record SuperAdminScoreRow(string PresentationTitle, string JudgePhone, string CriterionName, int Value, string UpdatedAt);

public sealed record SuperAdminScoresViewModel(string? Query, IReadOnlyList<SuperAdminScoreRow> Rows);

/// <summary>Reuses <see cref="HistoryEntry"/> as-is - read-only "Jurnal" tab, same
/// <see cref="Application.Interfaces.IHistoryRepository.GetRecentAsync"/> the desktop panel calls.</summary>
public sealed record SuperAdminJurnalViewModel(string? Query, IReadOnlyList<HistoryEntry> Entries);
