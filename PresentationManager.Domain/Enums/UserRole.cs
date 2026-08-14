namespace PresentationManager.Domain.Enums;

/// <summary>Desktop login roles — Operator/Admin/SuperAdmin/OrderOperator only. Presenters and Judges never
/// log into the WinForms app; they only interact through the Telegram bot and aren't represented by this
/// enum.</summary>
public enum UserRole
{
    Operator,
    Admin,
    SuperAdmin,

    /// <summary>"Tartib operatori" — a narrow-purpose role whose only capability is randomly assigning a
    /// project's presentation order (<see cref="PresentationManager.Application.Services.PresentationQueueService.RandomizeOrderAsync"/>),
    /// broadcast live to the main Operator's queue via SignalR. Deliberately not given any of
    /// Operator/Admin/SuperAdmin's other permissions.</summary>
    OrderOperator
}
