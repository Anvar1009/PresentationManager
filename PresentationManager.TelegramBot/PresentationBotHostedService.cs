using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PresentationManager.TelegramBot;

/// <summary>Two roles live here, routed purely by who's already known to the system when a chat says
/// /start: a <b>Presenter</b> uploads presentations into a project's queue (see
/// <see cref="HandleDocumentAsync"/>), and a linked <b>Admin</b> gets a read/report + basic-management
/// mirror of the desktop Admin panel (see <see cref="ShowAdminMainMenuAsync"/> onward) — the only one of the
/// two that isn't Telegram-native: an Admin must first link their desktop account via the "Botga ulash"
/// one-time code (<see cref="AdminLinkService"/>), whereas Presenter identities live entirely in
/// Telegram-side tables. A chat already known as a <b>Judge</b> (someone Admin assigned via the Admin
/// panel's "Hakamlar" dialog, <c>JudgeService.AssignAsync</c>) is instead redirected to the Judge web
/// platform (see <see cref="ShowJudgeWebRedirectAsync"/>) - in-chat scoring was removed once that platform
/// shipped (Phase 6 of the modernization concept). This class still subscribes to
/// <c>JudgeService.JudgeAssigned</c> to notify that person the moment Admin assigns them
/// (<see cref="OnJudgeAssignedAsync"/>). Identity priority on a plain /start is Admin, then Judge, then
/// Presenter (see <see cref="BeginAsync"/>) - whichever role this chat has always wins over the others. Runs
/// as PresentationManager.BotService's one hosted service (its own process/systemd unit, not embedded in the
/// WinForms admin app), talking to the database directly through the same Infrastructure repositories/
/// Application services AdminForm uses - a submitted file lands in the database and managed file storage
/// exactly the same way <c>AdminForm.OnAddClick</c> does.</summary>
public sealed class PresentationBotHostedService : BackgroundService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ppt", ".pptx", ".pdf" };

    private static readonly ReplyKeyboardMarkup ContactRequestKeyboard = new(
        new[] { new KeyboardButton("📱 Kontaktni ulashish") { RequestContact = true } })
    {
        ResizeKeyboard = true,
        OneTimeKeyboard = true
    };

    /// <summary>Persistent bottom panel shown to judges (deliberately NOT <c>OneTimeKeyboard</c>, unlike
    /// <see cref="ContactRequestKeyboard"/>, so it stays docked at the bottom of the chat across every
    /// message) — tapping "📋 Loyihalar" jumps straight back to the project/presentation menu without
    /// needing /start.</summary>
    /// <summary>Public (not just internal) so both <see cref="JudgeAssignmentNotifier"/> (BotService's own
    /// linked-Admin-mirror assignment flow) and PresentationManager.API.Controllers.JudgesController
    /// (PresentationManager.UI's desktop assignment flow) can reuse the exact same keyboard when pushing a
    /// judge-assignment notification, instead of duplicating this definition in a second assembly.</summary>
    public static readonly ReplyKeyboardMarkup JudgeMainKeyboard = new(
        new[] { new KeyboardButton("📋 Loyihalar") })
    {
        ResizeKeyboard = true
    };

    private const string JudgeProjectsButtonText = "📋 Loyihalar";

    /// <summary>Persistent bottom panel shown to a linked Admin — mirrors <see cref="JudgeMainKeyboard"/>,
    /// jumping straight back to <see cref="ShowAdminMainMenuAsync"/> without needing /start.</summary>
    private static readonly ReplyKeyboardMarkup AdminMainKeyboard = new(
        new[] { new KeyboardButton("🗂 Admin panel") })
    {
        ResizeKeyboard = true
    };

    private const string AdminMenuButtonText = "🗂 Admin panel";

    /// <summary>Persistent bottom panel shown to a registered presenter — mirrors <see cref="JudgeMainKeyboard"/>/
    /// <see cref="AdminMainKeyboard"/>. Tapping "📤 Taqdimot jo'natish" re-enters the project-selection flow
    /// (<see cref="ShowAssignedProjectsOrWaitAsync"/>) without needing to retype /start, including to submit a
    /// second presentation after the first one already succeeded. Public (not just internal) for the same
    /// reason as <see cref="JudgeMainKeyboard"/> - PresentationManager.API's PresenterAssignmentsController.Add
    /// and Controllers.Web.AdminController.AssignPresenter both attach it to the assignment notification so the
    /// button is already docked the moment a presenter is approved, even before their next /start.</summary>
    public static readonly ReplyKeyboardMarkup PresenterMainKeyboard = new(
        new[] { new KeyboardButton("📤 Taqdimot jo'natish") })
    {
        ResizeKeyboard = true
    };

    private const string PresenterSubmitButtonText = "📤 Taqdimot jo'natish";

    private readonly PresentationBotOptions _options;
    private readonly ProjectService _projectService;
    private readonly PresentationQueueService _queueService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IPresenterRepository _presenterRepository;
    private readonly JudgeService _judgeService;
    private readonly ScoreService _scoreService;
    private readonly CriterionService _criterionService;
    private readonly UserService _userService;
    private readonly AdminLinkService _adminLinkService;
    private readonly PresenterAssignmentService _presenterAssignmentService;

    /// <summary>Presenter upload flow state, per chat.</summary>
    private readonly ConcurrentDictionary<long, ChatSession> _sessions = new();

    /// <summary>Linked-Admin reporting/management flow state, per chat — see <see cref="AdminSession"/>.</summary>
    private readonly ConcurrentDictionary<long, AdminSession> _adminSessions = new();

    public PresentationBotHostedService(
        IOptions<PresentationBotOptions> options,
        ProjectService projectService,
        PresentationQueueService queueService,
        ISettingsRepository settingsRepository,
        IPresenterRepository presenterRepository,
        JudgeService judgeService,
        ScoreService scoreService,
        CriterionService criterionService,
        UserService userService,
        AdminLinkService adminLinkService,
        PresenterAssignmentService presenterAssignmentService)
    {
        _options = options.Value;
        _projectService = projectService;
        _queueService = queueService;
        _settingsRepository = settingsRepository;
        _presenterRepository = presenterRepository;
        _judgeService = judgeService;
        _scoreService = scoreService;
        _criterionService = criterionService;
        _userService = userService;
        _adminLinkService = adminLinkService;
        _presenterAssignmentService = presenterAssignmentService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No token configured (e.g. not yet set up in appsettings.Local.json) - stay quietly off instead of
        // crashing the whole app, since the token is a per-deployment secret and shouldn't be required just
        // to run the WinForms side of things.
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            return;
        }

        var botClient = new TelegramBotClient(_options.Token);
        var receiverOptions = new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery] };

        botClient.StartReceiving(HandleUpdateAsync, HandlePollingErrorAsync, receiverOptions, stoppingToken);

        // StartReceiving itself doesn't block (it dispatches to the thread pool) - keep this hosted service
        // "running" until the app shuts down, at which point stoppingToken cancels this delay.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on normal app shutdown.
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is { } message)
            {
                await HandleMessageAsync(botClient, message, ct);
            }
            else if (update.CallbackQuery is { } callbackQuery)
            {
                await HandleCallbackQueryAsync(botClient, callbackQuery, ct);
            }
        }
        catch (Exception ex)
        {
            // A single malformed/unexpected update must never take the whole polling loop down.
            Debug.WriteLine($"Telegram bot update handling failed: {ex}");
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
    {
        Debug.WriteLine($"Telegram bot polling error: {exception}");
        return Task.CompletedTask;
    }

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        // A Telegram deep link (t.me/bot?start=TOKEN) arrives as literal text "/start TOKEN" - handled before
        // the plain "/start" branch below, since that one deliberately ignores anything after the command.
        if (message.Text is { } startText && startText.StartsWith("/start ", StringComparison.Ordinal))
        {
            var token = startText["/start ".Length..].Trim();
            _sessions.TryRemove(chatId, out _);
            _adminSessions.TryRemove(chatId, out _);
            await HandleAdminLinkTokenAsync(botClient, chatId, token, message.From?.Username, ct);
            return;
        }

        if (message.Text is "/start" or "/cancel" or JudgeProjectsButtonText or AdminMenuButtonText or PresenterSubmitButtonText)
        {
            _sessions.TryRemove(chatId, out _);
            _adminSessions.TryRemove(chatId, out _);
            await BeginAsync(botClient, chatId, message.From?.Username, ct);
            return;
        }

        if (_adminSessions.ContainsKey(chatId))
        {
            await HandleAdminMessageAsync(botClient, chatId, message, ct);
            return;
        }

        if (!_sessions.TryGetValue(chatId, out var session))
        {
            await BeginAsync(botClient, chatId, message.From?.Username, ct);
            return;
        }

        switch (session.Step)
        {
            case SessionStep.AwaitingRegistrationFullName when !string.IsNullOrWhiteSpace(message.Text):
                session.FullName = message.Text.Trim();
                session.Step = SessionStep.AwaitingRegistrationContact;
                await botClient.SendMessage(chatId,
                    "Rahmat! Endi pastdagi tugma orqali telefon raqamingizni ulashing:",
                    replyMarkup: ContactRequestKeyboard, cancellationToken: ct);
                break;

            case SessionStep.AwaitingRegistrationContact when message.Contact is { } contact:
                await CompleteRegistrationAsync(botClient, chatId, session, contact, message.From?.Username, ct);
                break;

            case SessionStep.AwaitingRegistrationContact:
                await botClient.SendMessage(chatId, "Iltimos, pastdagi \"Kontaktni ulashish\" tugmasini bosing.", cancellationToken: ct);
                break;

            case SessionStep.AwaitingTitle when !string.IsNullOrWhiteSpace(message.Text):
                session.Title = message.Text.Trim();
                session.Step = SessionStep.AwaitingFile;
                await botClient.SendMessage(chatId, "Endi taqdimot faylini yuboring (.ppt, .pptx yoki .pdf):", cancellationToken: ct);
                break;

            case SessionStep.AwaitingFile when message.Document is { } document:
                await HandleDocumentAsync(botClient, chatId, session, document, ct);
                break;

            case SessionStep.AwaitingFile:
                await botClient.SendMessage(chatId, "Iltimos, .ppt, .pptx yoki .pdf formatidagi faylni yuboring.", cancellationToken: ct);
                break;

            default:
                await botClient.SendMessage(chatId, "Iltimos, kerakli ma'lumotni kiriting yoki /start bosing.", cancellationToken: ct);
                break;
        }
    }

    /// <summary>Entry point for both the very first message from a chat and every subsequent /start. A chat
    /// already linked to an Admin account takes top priority, then judges (someone Admin already assigned
    /// always means judge, even if that same person also uploads presentations elsewhere) - then
    /// already-registered presenters skip straight to project selection - brand new chats go through the
    /// one-time full name + contact-share registration, which always lands them as a Presenter (becoming a
    /// Judge only happens afterward, when Admin assigns them from the Admin panel - see
    /// <see cref="OnJudgeAssignedAsync"/>).</summary>
    private async Task BeginAsync(ITelegramBotClient botClient, long chatId, string? telegramUsername, CancellationToken ct)
    {
        var linkedUser = await _userService.GetByTelegramChatIdAsync(chatId, ct);
        if (linkedUser is not null)
        {
            // Opportunistic self-heal: accounts linked before TelegramUsername started being captured (or
            // whose Telegram username has since changed) get it filled in/refreshed right here, on any
            // ordinary /start, instead of requiring the account to be re-linked from scratch.
            if (!string.IsNullOrEmpty(telegramUsername) &&
                !string.Equals(linkedUser.TelegramUsername, telegramUsername, StringComparison.OrdinalIgnoreCase))
            {
                await _userService.LinkTelegramChatAsync(linkedUser.Id, chatId, telegramUsername, ct);
                linkedUser.TelegramUsername = telegramUsername;
            }

            await RouteLinkedUserAsync(botClient, chatId, linkedUser, ct);
            return;
        }

        var judgeAssignments = await _judgeService.GetLinkedAssignmentsByChatIdAsync(chatId, ct);
        if (judgeAssignments.Count > 0)
        {
            await ShowJudgeWebRedirectAsync(botClient, chatId, ct);
            return;
        }

        var presenter = await _presenterRepository.GetByTelegramChatIdAsync(chatId, ct);
        if (presenter is not null)
        {
            await ShowAssignedProjectsOrWaitAsync(botClient, chatId, presenter.Id, presenter.FullName, ct);
            return;
        }

        _sessions[chatId] = new ChatSession { Step = SessionStep.AwaitingRegistrationFullName };
        await botClient.SendMessage(chatId,
            "Xush kelibsiz! Avval ro'yxatdan o'tamiz - bu faqat bir marta so'raladi.\nIsm-familyangizni kiriting:",
            cancellationToken: ct);
    }

    /// <summary>Routes an already-linked account to whatever it should see on a plain /start: the full
    /// Admin reporting/management menu for <see cref="UserRole.Admin"/>, or just a quiet confirmation for
    /// every other linked role (today, only <see cref="UserRole.Operator"/> - linked purely so
    /// "Parolni unutdingizmi?" has somewhere to deliver a reset code, with no bot-side menu of its own).</summary>
    private async Task RouteLinkedUserAsync(ITelegramBotClient botClient, long chatId, PresentationManager.Domain.Entities.User user, CancellationToken ct)
    {
        if (user.Role == UserRole.Admin)
        {
            await ShowAdminMainMenuAsync(botClient, chatId, user, ct);
            return;
        }

        await botClient.SendMessage(chatId,
            $"✅ Hisobingiz ({user.FullName}) ulangan. Parolni tiklash kodlari shu Telegram chatga yuboriladi.",
            cancellationToken: ct);
    }

    /// <summary>Consumes a "Botga ulash" deep-link token (see <see cref="AdminLinkService"/>) and links this
    /// chat to the Admin/Operator account that generated it. An invalid/expired token (e.g. the 15-minute
    /// window lapsed, or it was already used) falls back to the normal <see cref="BeginAsync"/> flow rather
    /// than leaving the chat stuck, since the token itself carries no other identity to recover.</summary>
    private async Task HandleAdminLinkTokenAsync(ITelegramBotClient botClient, long chatId, string token, string? telegramUsername, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(token) && await _adminLinkService.TryConsumeAsync(token, ct) is { } userId)
        {
            try
            {
                await _userService.LinkTelegramChatAsync(userId, chatId, telegramUsername, ct);
                var user = await _userService.GetByIdAsync(userId, ct);
                if (user is not null)
                {
                    await botClient.SendMessage(chatId, $"✅ Hisobingiz ({user.FullName}) muvaffaqiyatli ulandi!", cancellationToken: ct);
                    await RouteLinkedUserAsync(botClient, chatId, user, ct);
                    return;
                }
            }
            catch (Exception)
            {
                // Most likely this chat is already linked to a DIFFERENT account (TelegramChatId is unique) -
                // a fresh token from the account that actually owns this chat is the only fix, so send them
                // back to the normal flow rather than silently doing nothing.
                await botClient.SendMessage(chatId, "❌ Bu Telegram hisob allaqachon boshqa akkauntga ulangan.", cancellationToken: ct);
                await BeginAsync(botClient, chatId, telegramUsername, ct);
                return;
            }
        }

        await botClient.SendMessage(chatId, "❌ Ulash havolasi yaroqsiz yoki muddati o'tgan. Admin paneldan qaytadan urinib ko'ring.", cancellationToken: ct);
        await BeginAsync(botClient, chatId, telegramUsername, ct);
    }

    private async Task CompleteRegistrationAsync(
        ITelegramBotClient botClient, long chatId, ChatSession session, Contact contact, string? telegramUsername, CancellationToken ct)
    {
        var presenter = await _presenterRepository.AddAsync(new Presenter
        {
            TelegramChatId = chatId,
            FullName = session.FullName,
            PhoneNumber = contact.PhoneNumber,
            TelegramUsername = telegramUsername
        }, ct);

        await botClient.SendMessage(chatId, "✅ Ro'yxatdan o'tish yakunlandi!", replyMarkup: PresenterMainKeyboard, cancellationToken: ct);
        await ShowAssignedProjectsOrWaitAsync(botClient, chatId, presenter.Id, presenter.FullName, ct);
    }

    // ---------- Presenter upload flow ----------

    /// <summary>Only projects Admin has explicitly approved this presenter for (<see cref="PresenterAssignmentService.GetAssignedProjectsAsync"/>)
    /// are offered here - a completed bot registration alone isn't enough, matching
    /// <see cref="PresentationQueueService.AddAsync"/>'s own server-side requirement that the upload's
    /// <c>presenterId</c> actually be assigned to the chosen project. Showing every project here regardless
    /// (as this used to) meant a presenter could pick and upload to a project they weren't approved for, only
    /// to have that same check reject it - see <see cref="HandleDocumentAsync"/>'s own error handling for what
    /// they now see when that happens instead of the request silently going nowhere.</summary>
    private async Task ShowAssignedProjectsOrWaitAsync(ITelegramBotClient botClient, long chatId, int presenterId, string fullName, CancellationToken ct)
    {
        var projects = await _presenterAssignmentService.GetAssignedProjectsAsync(presenterId, ct);
        if (projects.Count == 0)
        {
            await botClient.SendMessage(chatId,
                "Hozircha sizga biriktirilgan loyiha yo'q. Administrator sizni loyihaga tasdiqlagach, shu yerga xabar keladi va taqdimot yuborishingiz mumkin bo'ladi.",
                replyMarkup: PresenterMainKeyboard, cancellationToken: ct);
            return;
        }

        await ShowProjectListAsync(botClient, chatId, presenterId, fullName, projects, ct);
    }

    private async Task ShowProjectListAsync(ITelegramBotClient botClient, long chatId, int presenterId, string fullName, List<Project> projects, CancellationToken ct)
    {
        _sessions[chatId] = new ChatSession { Step = SessionStep.AwaitingProject, PresenterId = presenterId, FullName = fullName };

        var buttons = projects
            .Select(p => new[] { InlineKeyboardButton.WithCallbackData(p.Name, $"project:{p.Id}") })
            .ToArray();

        // Sent as two messages, not one: Telegram can't attach both a persistent ReplyKeyboardMarkup (bottom
        // panel) and an InlineKeyboardMarkup (this message's own buttons) to the same SendMessage call - the
        // first re-docks PresenterMainKeyboard (harmless if it's already showing), the second carries the
        // actual project picker.
        await botClient.SendMessage(chatId, $"👋 {fullName}, xush kelibsiz!", replyMarkup: PresenterMainKeyboard, cancellationToken: ct);
        await botClient.SendMessage(chatId, "Taqdimot yuborish uchun loyihani tanlang:",
            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task HandleProjectSelectionCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !int.TryParse(data["project:".Length..], out var projectId))
        {
            return;
        }

        if (!_sessions.TryGetValue(chatId.Value, out var session) || session.PresenterId is not { } presenterId)
        {
            // Stale callback (e.g. app restarted since the button was shown, wiping in-memory sessions) -
            // ask the presenter to start over rather than proceeding with no known identity.
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Sessiya eskirgan, /start bosing.", cancellationToken: ct);
            return;
        }

        // Re-validated against the assigned list (not just "does this project exist") - the button itself
        // only ever came from that same filtered list in ShowProjectListAsync, but a stale callback (an old
        // message re-tapped after Admin revoked the approval in between) must not let the upload through on
        // the strength of a button alone.
        var projects = await _presenterAssignmentService.GetAssignedProjectsAsync(presenterId, ct);
        var project = projects.FirstOrDefault(p => p.Id == projectId);
        if (project is null)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Bu loyiha endi mavjud emas yoki siz unga biriktirilmagansiz.", cancellationToken: ct);
            return;
        }

        session.Step = SessionStep.AwaitingTitle;
        session.ProjectId = project.Id;
        session.ProjectName = project.Name;

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await botClient.SendMessage(chatId.Value, $"Loyiha: {project.Name}\nTaqdimot sarlavhasini kiriting:", cancellationToken: ct);
    }

    private async Task HandleDocumentAsync(ITelegramBotClient botClient, long chatId, ChatSession session, Document document, CancellationToken ct)
    {
        var extension = Path.GetExtension(document.FileName ?? string.Empty);
        if (!AllowedExtensions.Contains(extension))
        {
            await botClient.SendMessage(chatId, "Fayl formati noto'g'ri. Faqat .ppt, .pptx yoki .pdf qabul qilinadi.", cancellationToken: ct);
            return;
        }

        var fileType = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? PresentationFileType.Pdf : PresentationFileType.Pptx;
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");

        try
        {
            await using (var fileStream = File.Create(tempFilePath))
            {
                await botClient.GetInfoAndDownloadFile(document.FileId, fileStream, ct);
            }

            var settings = await _settingsRepository.GetAsync(ct);
            await _queueService.AddAsync(
                session.ProjectId, session.FullName, session.Title,
                tempFilePath, fileType,
                settings.DefaultPresentationTimeSeconds, settings.DefaultDiscussionTimeSeconds,
                extraDiscussionTimeSeconds: 0,
                presenterId: session.PresenterId, ct: ct);

            // Explicitly restates what was actually captured (project/title/file type) rather than just a
            // generic "qabul qilindi" - so the presenter can immediately catch a wrong title or a misread
            // file type instead of only finding out when Admin reviews the queue.
            var fileTypeLabel = fileType == PresentationFileType.Pdf ? "PDF" : "PowerPoint";
            var confirmation =
                "✅ Taqdimotingiz qabul qilindi!\n\n" +
                $"🏛 Loyiha: {session.ProjectName}\n" +
                $"📌 Nomi: {session.Title}\n" +
                $"📄 Fayl turi: {fileTypeLabel}\n\n" +
                "Yana yuborish uchun pastdagi \"📤 Taqdimot jo'natish\" tugmasini bosing.";

            // The reminder is best-effort - the project could in principle have been deleted in the moment
            // between picking it and finishing the upload; the upload itself already succeeded above
            // regardless, so a missing project here just means no reminder gets appended, not a failure.
            var projects = await _projectService.GetAllAsync(ct);
            var project = projects.FirstOrDefault(p => p.Id == session.ProjectId);
            if (project is not null)
            {
                confirmation += $"\n\n{EventReminderFormatter.Format(project)}";
            }

            await botClient.SendMessage(chatId, confirmation, replyMarkup: PresenterMainKeyboard, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Previously uncaught here - it propagated up to HandleUpdateAsync's own catch-all, which only
            // Debug.WriteLine's it (invisible outside an attached debugger), leaving the presenter with no
            // response at all after sending their file. The two realistic causes: AddAsync rejecting an
            // upload to a project this presenter isn't (or no longer is - Admin can revoke mid-upload)
            // approved for ("Siz bu loyihaga hali biriktirilmagansiz"), or GetInfoAndDownloadFile/
            // SaveFileAsync failing outright (the Telegram Bot API caps bot file downloads at 20MB, or a
            // local disk I/O error) - either way the presenter now sees exactly why instead of silence.
            await botClient.SendMessage(chatId, $"❌ Taqdimotni yuborishda xatolik yuz berdi: {ex.Message}", cancellationToken: ct);
        }
        finally
        {
            File.Delete(tempFilePath);
            _sessions.TryRemove(chatId, out _);
        }
    }

    // ---------- Judge web platform redirect ----------

    /// <summary>Replaces the old in-chat judge scoring flow (Phase 6 of the modernization concept) - a judge
    /// now scores from PresentationManager.API's Cookie-authenticated web pages
    /// (Controllers\Web\JudgeController) instead of Telegram buttons, so this just points them there. Sent
    /// both on a fresh /start and every time the persistent "📋 Loyihalar" button is tapped - there's no
    /// in-chat state left to resume, so both cases are identical.</summary>
    private async Task ShowJudgeWebRedirectAsync(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        var message = string.IsNullOrEmpty(_options.JudgeWebBaseUrl)
            ? "🧑‍⚖️ Endi hakamlar veb-sahifa orqali baholaydi. Kirish manzilini administratordan so'rang."
            : $"🧑‍⚖️ Endi hakamlar veb-sahifa orqali baholaydi:\n{_options.JudgeWebBaseUrl.TrimEnd('/')}/Account/Login\n\nLogin va parolingiz o'zgarmagan.";

        await botClient.SendMessage(chatId, message, replyMarkup: JudgeMainKeyboard, cancellationToken: ct);
    }

    // ---------- Admin reporting/management flow ----------

    private async Task ShowAdminMainMenuAsync(ITelegramBotClient botClient, long chatId, PresentationManager.Domain.Entities.User user, CancellationToken ct)
    {
        _adminSessions[chatId] = new AdminSession { Step = AdminStep.MainMenu, UserId = user.Id };

        var projects = await _projectService.GetByCreatorAsync(user.Id, ct);
        var buttons = projects
            .Select(p => new[] { InlineKeyboardButton.WithCallbackData(p.Name, $"aproj:{p.Id}") })
            .ToList();
        buttons.Add([InlineKeyboardButton.WithCallbackData("➕ Yangi loyiha", "anewproj")]);

        await botClient.SendMessage(chatId, $"👋 Xush kelibsiz, {user.FullName}!", replyMarkup: AdminMainKeyboard, cancellationToken: ct);
        await botClient.SendMessage(chatId, projects.Count == 0 ? "Hozircha loyihalaringiz yo'q." : "📁 Loyihalaringiz:",
            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task ShowAdminProjectMenuAsync(ITelegramBotClient botClient, long chatId, int projectId, string projectName, CancellationToken ct)
    {
        if (_adminSessions.TryGetValue(chatId, out var session))
        {
            session.ProjectId = projectId;
            session.ProjectName = projectName;
            session.Step = AdminStep.ProjectMenu;
        }

        var buttons = new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("👥 Qatnashchilar", $"aparts:{projectId}") },
            new[] { InlineKeyboardButton.WithCallbackData("📑 Taqdimotlar", $"apres:{projectId}") },
            new[] { InlineKeyboardButton.WithCallbackData("🏆 Yakuniy baholar", $"ascores:{projectId}") },
            new[] { InlineKeyboardButton.WithCallbackData("📐 Mezonlar", $"acrit:{projectId}") },
            new[] { InlineKeyboardButton.WithCallbackData("⚖️ Hakamlar", $"ajudg:{projectId}") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Loyihalar ro'yxati", "amain") }
        };

        await botClient.SendMessage(chatId, $"📁 {projectName}", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task ShowAdminParticipantsAsync(ITelegramBotClient botClient, long chatId, int projectId, CancellationToken ct)
    {
        var project = (await _projectService.GetAllAsync(ct)).FirstOrDefault(p => p.Id == projectId);
        var participants = await _projectService.GetParticipantsAsync(projectId, ct);

        var text = participants.Count == 0
            ? "Hozircha qatnashchilar yo'q."
            : string.Join('\n', participants.Select((p, i) =>
                $"{i + 1}. {p.FullName}{(p.PhoneNumber is { } phone ? $" — {phone}" : string.Empty)} ({p.PresentationCount} ta taqdimot)"));

        await botClient.SendMessage(chatId, $"👥 {project?.Name} — Qatnashchilar\n\n{text}",
            replyMarkup: BackToProjectKeyboard(projectId), cancellationToken: ct);
    }

    private async Task ShowAdminPresentationsAsync(ITelegramBotClient botClient, long chatId, int projectId, CancellationToken ct)
    {
        var project = (await _projectService.GetAllAsync(ct)).FirstOrDefault(p => p.Id == projectId);
        var presentations = await _queueService.GetAllAsync(projectId, ct);

        var text = presentations.Count == 0
            ? "Hozircha taqdimotlar yo'q."
            : string.Join('\n', presentations.Select(p =>
                $"{p.OrderNumber + 1}. {p.FullName} — {p.Title} [{UzbekText.StatusLabel(p.Status)}]"));

        await botClient.SendMessage(chatId, $"📑 {project?.Name} — Taqdimotlar\n\n{text}",
            replyMarkup: BackToProjectKeyboard(projectId), cancellationToken: ct);
    }

    /// <summary>Mirrors <c>AdminPanelForm</c>'s "Yakuniy baholar" tab: per criterion, the average across every
    /// judge who scored it (see <c>ScoreService.GetFinalScoresAsync</c>), plus the summed total.</summary>
    private async Task ShowAdminScoresAsync(ITelegramBotClient botClient, long chatId, int projectId, CancellationToken ct)
    {
        var project = (await _projectService.GetAllAsync(ct)).FirstOrDefault(p => p.Id == projectId);
        var criteria = await _criterionService.GetByProjectIdAsync(projectId, ct);
        var summaries = await _scoreService.GetFinalScoresAsync(projectId, ct);

        if (summaries.Count == 0)
        {
            await botClient.SendMessage(chatId, $"🏆 {project?.Name} — Yakuniy baholar\n\nHozircha taqdimotlar yo'q.",
                replyMarkup: BackToProjectKeyboard(projectId), cancellationToken: ct);
            return;
        }

        var lines = summaries.Select(s =>
        {
            var perCriterion = criteria.Count == 0
                ? string.Empty
                : "\n   " + string.Join(", ", criteria.Select(c => $"{c.Name}: {s.AverageByCriterionId.GetValueOrDefault(c.Id, 0):0.##}"));
            return $"• {s.PresenterFullName} — {s.Title}{perCriterion}\n   Jami: {s.Total:0.##}";
        });

        await botClient.SendMessage(chatId, $"🏆 {project?.Name} — Yakuniy baholar\n\n{string.Join("\n\n", lines)}",
            replyMarkup: BackToProjectKeyboard(projectId), cancellationToken: ct);
    }

    private async Task ShowAdminCriteriaAsync(ITelegramBotClient botClient, long chatId, int projectId, CancellationToken ct)
    {
        var criteria = await _criterionService.GetByProjectIdAsync(projectId, ct);
        var text = criteria.Count == 0
            ? "Hozircha mezonlar yo'q."
            : string.Join('\n', criteria.Select((c, i) => $"{i + 1}. {c.Name} (max {c.MaxScore})"));

        var buttons = new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("➕ Mezon qo'shish", $"aaddcrit:{projectId}") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Orqaga", $"aproj:{projectId}") }
        };

        await botClient.SendMessage(chatId, $"📐 Mezonlar\n\n{text}", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task ShowAdminJudgesAsync(ITelegramBotClient botClient, long chatId, int projectId, CancellationToken ct)
    {
        var judges = await _judgeService.GetByProjectIdAsync(projectId, ct);
        var text = judges.Count == 0
            ? "Hozircha hakamlar yo'q."
            : string.Join('\n', judges.Select((j, i) => $"{i + 1}. {j.FullName} — {j.PhoneNumber}"));

        var buttons = new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("➕ Hakam biriktirish", $"aaddjudg:{projectId}") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Orqaga", $"aproj:{projectId}") }
        };

        await botClient.SendMessage(chatId, $"⚖️ Hakamlar\n\n{text}", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private static InlineKeyboardMarkup BackToProjectKeyboard(int projectId) =>
        new(new[] { new[] { InlineKeyboardButton.WithCallbackData("⬅️ Orqaga", $"aproj:{projectId}") } });

    private static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(text)
            && DateOnly.TryParseExact(text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Text-message dispatch for every multi-step Admin flow (new project, new criterion, judge
    /// assignment by phone) — mirrors <see cref="HandleMessageAsync"/>'s <c>switch (session.Step)</c> for the
    /// presenter flow, kept separate since the two step enums don't overlap.</summary>
    private async Task HandleAdminMessageAsync(ITelegramBotClient botClient, long chatId, Message message, CancellationToken ct)
    {
        if (!_adminSessions.TryGetValue(chatId, out var session))
        {
            return;
        }

        var text = message.Text?.Trim();

        switch (session.Step)
        {
            case AdminStep.CreatingProjectName when !string.IsNullOrWhiteSpace(text):
                session.NewProjectName = text!;
                session.Step = AdminStep.CreatingProjectStartDate;
                await botClient.SendMessage(chatId, "Boshlanish sanasini kiriting (kun.oy.yil, masalan 15.08.2026):", cancellationToken: ct);
                break;

            case AdminStep.CreatingProjectStartDate when TryParseDate(text, out var startDate):
                session.NewProjectStartDate = startDate;
                session.Step = AdminStep.CreatingProjectEndDate;
                await botClient.SendMessage(chatId,
                    "Tugash sanasini kiriting (kun.oy.yil) — bir kunlik bo'lsa xuddi shu sanani qayta yuboring:", cancellationToken: ct);
                break;

            case AdminStep.CreatingProjectStartDate:
                await botClient.SendMessage(chatId, "Sana formati noto'g'ri. Masalan: 15.08.2026", cancellationToken: ct);
                break;

            case AdminStep.CreatingProjectEndDate when TryParseDate(text, out var endDate) && endDate >= session.NewProjectStartDate:
                session.NewProjectEndDate = endDate;
                session.Step = AdminStep.CreatingProjectLocation;
                await botClient.SendMessage(chatId, "Manzilni kiriting (yoki o'tkazib yuborish uchun \"-\" yuboring):", cancellationToken: ct);
                break;

            case AdminStep.CreatingProjectEndDate:
                await botClient.SendMessage(chatId, "Sana formati noto'g'ri yoki boshlanish sanasidan oldin. Qaytadan kiriting:", cancellationToken: ct);
                break;

            case AdminStep.CreatingProjectLocation when !string.IsNullOrWhiteSpace(text):
                await CreateProjectFromSessionAsync(botClient, chatId, session, text == "-" ? null : text, ct);
                break;

            case AdminStep.AddingCriterionName when !string.IsNullOrWhiteSpace(text):
                session.NewCriterionName = text!;
                session.Step = AdminStep.AddingCriterionMaxScore;
                await botClient.SendMessage(chatId, "Maksimal ballni kiriting (masalan 10):", cancellationToken: ct);
                break;

            case AdminStep.AddingCriterionMaxScore when int.TryParse(text, out var maxScore):
                await CreateCriterionFromSessionAsync(botClient, chatId, session, maxScore, ct);
                break;

            case AdminStep.AddingCriterionMaxScore:
                await botClient.SendMessage(chatId, "Iltimos, butun son kiriting (masalan 10).", cancellationToken: ct);
                break;

            case AdminStep.AssigningJudgePhone when !string.IsNullOrWhiteSpace(text):
                await SearchAndAssignJudgeAsync(botClient, chatId, session, text!, ct);
                break;

            default:
                await botClient.SendMessage(chatId, "Iltimos, tugmalardan birini tanlang yoki so'ralgan ma'lumotni kiriting.", cancellationToken: ct);
                break;
        }
    }

    private async Task CreateProjectFromSessionAsync(ITelegramBotClient botClient, long chatId, AdminSession session, string? location, CancellationToken ct)
    {
        try
        {
            var project = await _projectService.CreateAsync(
                session.NewProjectName, session.NewProjectStartDate, session.NewProjectEndDate, null, location,
                session.UserId, ct);

            await botClient.SendMessage(chatId, $"✅ \"{project.Name}\" loyihasi yaratildi!", cancellationToken: ct);
            await ShowAdminProjectMenuAsync(botClient, chatId, project.Id, project.Name, ct);
        }
        catch (Exception ex)
        {
            session.Step = AdminStep.MainMenu;
            await botClient.SendMessage(chatId, $"❌ {ex.Message}", cancellationToken: ct);
        }
    }

    private async Task CreateCriterionFromSessionAsync(ITelegramBotClient botClient, long chatId, AdminSession session, int maxScore, CancellationToken ct)
    {
        try
        {
            await _criterionService.CreateAsync(session.ProjectId, session.NewCriterionName, maxScore, ct);
            await botClient.SendMessage(chatId, "✅ Mezon qo'shildi.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await botClient.SendMessage(chatId, $"❌ {ex.Message}", cancellationToken: ct);
        }

        session.Step = AdminStep.ProjectMenu;
        await ShowAdminCriteriaAsync(botClient, chatId, session.ProjectId, ct);
    }

    /// <summary>Same candidate search <see cref="JudgeAssignForm"/> (the desktop equivalent) does - only
    /// already bot-registered presenters, excluding whoever's already a judge on this project - narrowed by a
    /// phone-number substring since the bot has no list-and-click UI as convenient as a desktop ListBox.</summary>
    private async Task SearchAndAssignJudgeAsync(ITelegramBotClient botClient, long chatId, AdminSession session, string phoneQuery, CancellationToken ct)
    {
        var registered = await _presenterRepository.GetAllAsync(ct);
        var existingJudges = await _judgeService.GetByProjectIdAsync(session.ProjectId, ct);
        var alreadyAssignedChatIds = existingJudges.Select(j => j.TelegramChatId).ToHashSet();

        var candidates = registered
            .Where(p => !alreadyAssignedChatIds.Contains(p.TelegramChatId))
            .Where(p => (p.PhoneNumber ?? string.Empty).Contains(phoneQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            await botClient.SendMessage(chatId,
                "Bunday raqam bilan ro'yxatdan o'tgan (va shu loyihaga hali tayinlanmagan) odam topilmadi. Avval kerakli odam botga /start bosib kontaktini ulashsin. Qaytadan urinib ko'ring yoki /cancel bosing:",
                cancellationToken: ct);
            return;
        }

        if (candidates.Count == 1)
        {
            await AssignJudgeAsync(botClient, chatId, session, candidates[0].Id, ct);
            return;
        }

        var buttons = candidates
            .Select(p => new[] { InlineKeyboardButton.WithCallbackData($"{p.FullName} — {p.PhoneNumber}", $"ajassign:{p.Id}") })
            .ToArray();
        await botClient.SendMessage(chatId, "Bir nechta mos odam topildi, birini tanlang:", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task AssignJudgeAsync(ITelegramBotClient botClient, long chatId, AdminSession session, int presenterId, CancellationToken ct)
    {
        try
        {
            await _judgeService.AssignAsync(session.ProjectId, presenterId, ct);
            await botClient.SendMessage(chatId, "✅ Hakam tayinlandi.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await botClient.SendMessage(chatId, $"❌ {ex.Message}", cancellationToken: ct);
        }

        session.Step = AdminStep.ProjectMenu;
        await ShowAdminJudgesAsync(botClient, chatId, session.ProjectId, ct);
    }

    // ---------- Admin callback dispatch ----------

    private async Task HandleAdminMainCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null)
        {
            return;
        }

        var user = await _userService.GetByTelegramChatIdAsync(chatId.Value, ct);
        if (user is null)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Sessiya eskirgan, /start bosing.", cancellationToken: ct);
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowAdminMainMenuAsync(botClient, chatId.Value, user, ct);
    }

    private async Task HandleAdminNewProjectCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !_adminSessions.TryGetValue(chatId.Value, out var session))
        {
            return;
        }

        session.Step = AdminStep.CreatingProjectName;
        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await botClient.SendMessage(chatId.Value, "Yangi loyiha nomini kiriting:", cancellationToken: ct);
    }

    private async Task HandleAdminProjectCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !int.TryParse(data["aproj:".Length..], out var projectId))
        {
            return;
        }

        var project = (await _projectService.GetAllAsync(ct)).FirstOrDefault(p => p.Id == projectId);
        if (project is null)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Bu loyiha endi mavjud emas.", cancellationToken: ct);
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowAdminProjectMenuAsync(botClient, chatId.Value, project.Id, project.Name, ct);
    }

    private async Task HandleAdminParticipantsCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !int.TryParse(data["aparts:".Length..], out var projectId))
        {
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowAdminParticipantsAsync(botClient, chatId.Value, projectId, ct);
    }

    private async Task HandleAdminPresentationsCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !int.TryParse(data["apres:".Length..], out var projectId))
        {
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowAdminPresentationsAsync(botClient, chatId.Value, projectId, ct);
    }

    private async Task HandleAdminScoresCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !int.TryParse(data["ascores:".Length..], out var projectId))
        {
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowAdminScoresAsync(botClient, chatId.Value, projectId, ct);
    }

    private async Task HandleAdminCriteriaCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !int.TryParse(data["acrit:".Length..], out var projectId))
        {
            return;
        }

        if (_adminSessions.TryGetValue(chatId.Value, out var session))
        {
            session.ProjectId = projectId;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowAdminCriteriaAsync(botClient, chatId.Value, projectId, ct);
    }

    private async Task HandleAdminJudgesCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !int.TryParse(data["ajudg:".Length..], out var projectId))
        {
            return;
        }

        if (_adminSessions.TryGetValue(chatId.Value, out var session))
        {
            session.ProjectId = projectId;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowAdminJudgesAsync(botClient, chatId.Value, projectId, ct);
    }

    private async Task HandleAdminAddCriterionCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !int.TryParse(data["aaddcrit:".Length..], out var projectId) || !_adminSessions.TryGetValue(chatId.Value, out var session))
        {
            return;
        }

        session.ProjectId = projectId;
        session.Step = AdminStep.AddingCriterionName;
        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await botClient.SendMessage(chatId.Value, "Mezon nomini kiriting:", cancellationToken: ct);
    }

    private async Task HandleAdminAddJudgeCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !int.TryParse(data["aaddjudg:".Length..], out var projectId) || !_adminSessions.TryGetValue(chatId.Value, out var session))
        {
            return;
        }

        session.ProjectId = projectId;
        session.Step = AdminStep.AssigningJudgePhone;
        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await botClient.SendMessage(chatId.Value,
            "Hakam etib tayinlash uchun ro'yxatdan o'tgan odamning telefon raqamini (yoki uning bir qismini) kiriting:",
            cancellationToken: ct);
    }

    private async Task HandleAdminJudgeAssignCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !_adminSessions.TryGetValue(chatId.Value, out var session) || !int.TryParse(data["ajassign:".Length..], out var presenterId))
        {
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await AssignJudgeAsync(botClient, chatId.Value, session, presenterId, ct);
    }

    // ---------- Callback dispatch ----------

    private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        var data = callbackQuery.Data;
        if (data is null)
        {
            return;
        }

        if (data.StartsWith("project:", StringComparison.Ordinal))
        {
            await HandleProjectSelectionCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data == "amain")
        {
            await HandleAdminMainCallbackAsync(botClient, callbackQuery, ct);
        }
        else if (data == "anewproj")
        {
            await HandleAdminNewProjectCallbackAsync(botClient, callbackQuery, ct);
        }
        else if (data.StartsWith("aproj:", StringComparison.Ordinal))
        {
            await HandleAdminProjectCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("aparts:", StringComparison.Ordinal))
        {
            await HandleAdminParticipantsCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("apres:", StringComparison.Ordinal))
        {
            await HandleAdminPresentationsCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("ascores:", StringComparison.Ordinal))
        {
            await HandleAdminScoresCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("acrit:", StringComparison.Ordinal))
        {
            await HandleAdminCriteriaCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("ajudg:", StringComparison.Ordinal))
        {
            await HandleAdminJudgesCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("aaddcrit:", StringComparison.Ordinal))
        {
            await HandleAdminAddCriterionCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("aaddjudg:", StringComparison.Ordinal))
        {
            await HandleAdminAddJudgeCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("ajassign:", StringComparison.Ordinal))
        {
            await HandleAdminJudgeAssignCallbackAsync(botClient, callbackQuery, data, ct);
        }
    }
}
