namespace PresentationManager.Domain.Entities;

/// <summary>A tenant boundary — every <see cref="User"/> (except <see cref="Enums.UserRole.SuperAdmin"/>, which
/// stays global) and every <see cref="Project"/> belongs to at most one of these. Created only by SuperAdmin
/// (see PresentationManager.API's SuperAdminController "Tashkilotlar" section), which also assigns the first
/// <see cref="Enums.UserRole.Manager"/> account for it.</summary>
public class Organization
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
