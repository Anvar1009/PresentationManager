using PresentationManager.Domain.Enums;

namespace PresentationManager.Domain.Entities;

/// <summary>A desktop login account — only Operator/Admin/SuperAdmin ever authenticate this way.</summary>
public class User
{
    public int Id { get; set; }

    public required string Username { get; set; }

    /// <summary>Packed "iterations.saltBase64.hashBase64" — see <c>PasswordHasher</c>.</summary>
    public required string PasswordHash { get; set; }

    public required string FullName { get; set; }

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
