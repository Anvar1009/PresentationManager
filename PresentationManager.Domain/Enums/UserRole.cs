namespace PresentationManager.Domain.Enums;

/// <summary>Desktop login roles — Operator/Admin/SuperAdmin only. Presenters and Judges never log into the
/// WinForms app; they only interact through the Telegram bot and aren't represented by this enum.</summary>
public enum UserRole
{
    Operator,
    Admin,
    SuperAdmin
}
