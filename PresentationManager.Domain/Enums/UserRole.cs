namespace PresentationManager.Domain.Enums;

/// <summary>Login roles — Operator/Admin/SuperAdmin log into the WinForms desktop app; Judge and
/// OrderOperator log into the web platform only (see PresentationManager.API's Account/Judge/Order MVC
/// controllers) and are deliberately rejected by the desktop LoginForm's role routing. Presenters still
/// never log in at all - they only interact through the Telegram bot and aren't represented by this enum.</summary>
public enum UserRole
{
    Operator,
    Admin,
    SuperAdmin,

    /// <summary>Web-only "Tartib operatori" role - a narrow-purpose account (created the same way as any
    /// other, via SuperAdmin's "+ Foydalanuvchi qo'shish") whose only capability, from
    /// PresentationManager.API's OrderController, is randomly assigning a project's presentation order
    /// (<see cref="PresentationManager.Application.Services.PresentationQueueService.RandomizeOrderAsync"/>),
    /// broadcast live to the main Operator's WinForms queue via SignalR. Deliberately not given any of
    /// Operator/Admin/SuperAdmin's other permissions, and - unlike <see cref="Judge"/> - not scoped to
    /// specific projects, since anyone holding this role is trusted to reorder any project.</summary>
    OrderOperator,

    /// <summary>Web-only judging role. A <see cref="PresentationManager.Domain.Entities.User"/> with this
    /// role is created the same way Admin/Operator accounts are (SuperAdmin's "+ Foydalanuvchi qo'shish",
    /// promoting an already Telegram-registered <see cref="PresentationManager.Domain.Entities.Presenter"/> -
    /// see <see cref="PresentationManager.Application.Services.UserService.CreateFromPresenterAsync"/>, which
    /// already copies <see cref="PresentationManager.Domain.Entities.User.TelegramChatId"/> from that
    /// presenter). That copied TelegramChatId is what lets the web login resolve which project(s) this judge
    /// is assigned to: it matches <see cref="PresentationManager.Domain.Entities.Judge.TelegramChatId"/> via
    /// the existing <see cref="PresentationManager.Application.Services.JudgeService.GetLinkedAssignmentsByChatIdAsync"/>
    /// - the very same lookup the Telegram bot's (now removed) in-chat scoring flow used to use.</summary>
    Judge
}
