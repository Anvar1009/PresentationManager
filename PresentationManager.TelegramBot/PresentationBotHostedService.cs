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

/// <summary>Three roles live here, routed purely by who's already known to the system when a chat says
/// /start: a <b>Presenter</b> uploads presentations into a project's queue (see
/// <see cref="HandleDocumentAsync"/>), a <b>Judge</b> scores presentations against a project's dynamic
/// criteria (see <see cref="ShowJudgePresentationsAsync"/> onward), and a linked <b>Admin</b> gets a
/// read/report + basic-management mirror of the desktop Admin panel (see
/// <see cref="ShowAdminMainMenuAsync"/> onward) — the only one of the three that isn't Telegram-native: an
/// Admin must first link their desktop account via the "Botga ulash" one-time code
/// (<see cref="AdminLinkService"/>), whereas Presenter/Judge identities live entirely in Telegram-side tables.
/// Presenter and Judge both start from the same one-time registration (full name, then sharing a Telegram
/// contact) and land as a Presenter — becoming a Judge only happens afterward, when Admin picks that
/// already-registered person from the Admin panel's "Hakamlar" dialog (<c>JudgeService.AssignAsync</c>); this
/// class subscribes to <c>JudgeService.JudgeAssigned</c> to push that person a notification the moment it
/// happens (<see cref="OnJudgeAssignedAsync"/>). Identity priority on a plain /start is Admin, then Judge,
/// then Presenter (see <see cref="BeginAsync"/>) - mirrors the existing Judge-over-Presenter precedent for
/// the same reason: whichever role this chat has always wins over the others. Runs embedded in the same
/// process as the WinForms admin app (started via <c>Program.cs</c>'s host), reusing its existing service
/// singletons - a submitted file lands in the database and managed file storage exactly the same way
/// <c>AdminForm.OnAddClick</c> does.</summary>
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
    /// <summary>Internal (not private) so <see cref="JudgeAssignmentNotifier"/> can reuse the exact same
    /// keyboard when pushing a judge-assignment notification, instead of duplicating this definition.</summary>
    internal static readonly ReplyKeyboardMarkup JudgeMainKeyboard = new(
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

    /// <summary>Presenter upload flow state, per chat.</summary>
    private readonly ConcurrentDictionary<long, ChatSession> _sessions = new();

    /// <summary>Judge scoring flow state, per chat — separate from <see cref="_sessions"/> since a chat is
    /// only ever in one flow at a time but the two shapes don't overlap.</summary>
    private readonly ConcurrentDictionary<long, JudgeSession> _judgeSessions = new();

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
        AdminLinkService adminLinkService)
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
            _judgeSessions.TryRemove(chatId, out _);
            _adminSessions.TryRemove(chatId, out _);
            await HandleAdminLinkTokenAsync(botClient, chatId, token, message.From?.Username, ct);
            return;
        }

        if (message.Text is "/start" or "/cancel" or JudgeProjectsButtonText or AdminMenuButtonText)
        {
            _sessions.TryRemove(chatId, out _);
            _judgeSessions.TryRemove(chatId, out _);
            _adminSessions.TryRemove(chatId, out _);
            await BeginAsync(botClient, chatId, message.From?.Username, ct);
            return;
        }

        if (_adminSessions.ContainsKey(chatId))
        {
            await HandleAdminMessageAsync(botClient, chatId, message, ct);
            return;
        }

        if (_judgeSessions.ContainsKey(chatId))
        {
            await HandleJudgeMessageAsync(botClient, chatId, ct);
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
            // Sent as its own message (a reply keyboard can't ride along on the inline-keyboard menu message
            // below) - keeps the "📋 Loyihalar" button docked at the bottom of the chat from here on, so
            // returning to this menu later never needs /start again.
            await botClient.SendMessage(chatId, "🧑‍⚖️ Hakam paneli", replyMarkup: JudgeMainKeyboard, cancellationToken: ct);
            await ShowJudgeProjectMenuAsync(botClient, chatId, judgeAssignments, ct);
            return;
        }

        var presenter = await _presenterRepository.GetByTelegramChatIdAsync(chatId, ct);
        if (presenter is not null)
        {
            await ShowProjectListAsync(botClient, chatId, presenter.Id, presenter.FullName, ct);
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

        await botClient.SendMessage(chatId, "✅ Ro'yxatdan o'tish yakunlandi!", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
        await ShowProjectListAsync(botClient, chatId, presenter.Id, presenter.FullName, ct);
    }

    // ---------- Presenter upload flow ----------

    private async Task ShowProjectListAsync(ITelegramBotClient botClient, long chatId, int presenterId, string fullName, CancellationToken ct)
    {
        var projects = await _projectService.GetAllAsync(ct);
        if (projects.Count == 0)
        {
            await botClient.SendMessage(chatId, "Hozircha loyihalar mavjud emas. Keyinroq urinib ko'ring.", cancellationToken: ct);
            return;
        }

        _sessions[chatId] = new ChatSession { Step = SessionStep.AwaitingProject, PresenterId = presenterId, FullName = fullName };

        var buttons = projects
            .Select(p => new[] { InlineKeyboardButton.WithCallbackData(p.Name, $"project:{p.Id}") })
            .ToArray();

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

        if (!_sessions.TryGetValue(chatId.Value, out var session))
        {
            // Stale callback (e.g. app restarted since the button was shown, wiping in-memory sessions) -
            // ask the presenter to start over rather than proceeding with no known identity.
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Sessiya eskirgan, /start bosing.", cancellationToken: ct);
            return;
        }

        var projects = await _projectService.GetAllAsync(ct);
        var project = projects.FirstOrDefault(p => p.Id == projectId);
        if (project is null)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Bu loyiha endi mavjud emas.", cancellationToken: ct);
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

            var confirmation = $"✅ Taqdimotingiz \"{session.ProjectName}\" loyihasiga qabul qilindi. Yana yuborish uchun /start bosing.";

            // The reminder is best-effort - the project could in principle have been deleted in the moment
            // between picking it and finishing the upload; the upload itself already succeeded above
            // regardless, so a missing project here just means no reminder gets appended, not a failure.
            var projects = await _projectService.GetAllAsync(ct);
            var project = projects.FirstOrDefault(p => p.Id == session.ProjectId);
            if (project is not null)
            {
                confirmation += $"\n\n{FormatEventReminder(project)}";
            }

            await botClient.SendMessage(chatId, confirmation, cancellationToken: ct);
        }
        finally
        {
            File.Delete(tempFilePath);
            _sessions.TryRemove(chatId, out _);
        }
    }

    /// <summary>Tells the presenter when/where the event they just submitted a presentation for actually is
    /// - the info the operator captured on the project itself (<see cref="ProjectEditForm"/> in AdminForm).</summary>
    private static string FormatEventReminder(Project project)
    {
        var dateText = project.EventStartDate == project.EventEndDate
            ? project.EventStartDate.ToString("dd.MM.yyyy")
            : $"{project.EventStartDate:dd.MM.yyyy} - {project.EventEndDate:dd.MM.yyyy}";

        var lines = new List<string> { $"📅 Sana: {dateText}" };
        if (project.EventTime is { } time)
        {
            lines.Add($"🕒 Vaqti: {time:HH:mm}");
        }
        if (!string.IsNullOrWhiteSpace(project.Location))
        {
            lines.Add($"📍 Manzil: {project.Location}");
        }

        return string.Join('\n', lines);
    }

    // ---------- Judge scoring flow ----------

    private async Task ShowJudgeProjectMenuAsync(ITelegramBotClient botClient, long chatId, List<Judge> assignments, CancellationToken ct)
    {
        if (assignments.Count == 1)
        {
            await ShowJudgePresentationsAsync(botClient, chatId, assignments[0], ct);
            return;
        }

        var projects = await _projectService.GetAllAsync(ct);
        var buttons = assignments
            .Select(a => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    projects.FirstOrDefault(p => p.Id == a.ProjectId)?.Name ?? "?", $"jproj:{a.Id}")
            })
            .ToArray();

        _judgeSessions[chatId] = new JudgeSession { Step = JudgeStep.SelectingProject };
        await botClient.SendMessage(chatId, "Qaysi loyiha bo'yicha baholaysiz?", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task ShowJudgePresentationsAsync(ITelegramBotClient botClient, long chatId, Judge judge, CancellationToken ct)
    {
        var presentations = await _queueService.GetAllAsync(judge.ProjectId, ct);
        if (presentations.Count == 0)
        {
            await botClient.SendMessage(chatId, "Bu loyihada hali taqdimotlar yo'q.", cancellationToken: ct);
            return;
        }

        var criteria = await _criterionService.GetByProjectIdAsync(judge.ProjectId, ct);
        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var presentation in presentations)
        {
            var progress = await _scoreService.GetJudgeProgressAsync(presentation.Id, judge.Id, ct);
            var mark = criteria.Count > 0 && criteria.All(c => progress.ContainsKey(c.Id)) ? "✅ " : string.Empty;
            buttons.Add([InlineKeyboardButton.WithCallbackData($"{mark}{presentation.FullName} - {presentation.Title}", $"jpres:{presentation.Id}")]);
        }

        _judgeSessions[chatId] = new JudgeSession { Step = JudgeStep.SelectingPresentation, JudgeId = judge.Id, ProjectId = judge.ProjectId };
        await botClient.SendMessage(chatId, "Baholash uchun taqdimotni tanlang:", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task ShowCriteriaMenuAsync(ITelegramBotClient botClient, long chatId, JudgeSession session, CancellationToken ct)
    {
        var criteria = await _criterionService.GetByProjectIdAsync(session.ProjectId, ct);
        var progress = await _scoreService.GetJudgeProgressAsync(session.PresentationId, session.JudgeId, ct);

        // Inline buttons have no real background color in the Bot API - a leading green/white square is the
        // closest equivalent, giving the same "scored vs. not" read at a glance the operator asked for.
        var buttons = criteria
            .Select(c =>
            {
                var isScored = progress.TryGetValue(c.Id, out var value);
                var mark = isScored ? "🟩" : "⬜";
                var scoreText = isScored ? $"{value}/{c.MaxScore}" : $"—/{c.MaxScore}";
                return new[] { InlineKeyboardButton.WithCallbackData($"{mark} {c.Name} ({scoreText})", $"jcrit:{c.Id}") };
            })
            .ToList();
        buttons.Add([InlineKeyboardButton.WithCallbackData("✅ Yakunlash", "jdone")]);

        await botClient.SendMessage(chatId, "Mezonni tanlang va ball qo'ying:", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task HandleJudgeProjectCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !int.TryParse(data["jproj:".Length..], out var judgeRowId))
        {
            return;
        }

        var assignments = await _judgeService.GetLinkedAssignmentsByChatIdAsync(chatId.Value, ct);
        var judge = assignments.FirstOrDefault(a => a.Id == judgeRowId);
        if (judge is null)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Topilmadi, /start bosing.", cancellationToken: ct);
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowJudgePresentationsAsync(botClient, chatId.Value, judge, ct);
    }

    private async Task HandleJudgePresentationCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !_judgeSessions.TryGetValue(chatId.Value, out var session))
        {
            return;
        }

        if (!int.TryParse(data["jpres:".Length..], out var presentationId))
        {
            return;
        }

        session.PresentationId = presentationId;
        session.Step = JudgeStep.ScoringPresentation;

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowCriteriaMenuAsync(botClient, chatId.Value, session, ct);
    }

    /// <summary>Shows the 0..MaxScore buttons for one criterion — the judge taps a score directly instead of
    /// typing it, five per row so even a generous max score (e.g. 20) doesn't scroll forever.</summary>
    private async Task ShowScoreButtonsAsync(ITelegramBotClient botClient, long chatId, JudgeSession session, int criterionId, CancellationToken ct)
    {
        var criteria = await _criterionService.GetByProjectIdAsync(session.ProjectId, ct);
        var criterion = criteria.FirstOrDefault(c => c.Id == criterionId);
        if (criterion is null)
        {
            await botClient.SendMessage(chatId, "Bu mezon endi mavjud emas.", cancellationToken: ct);
            await ShowCriteriaMenuAsync(botClient, chatId, session, ct);
            return;
        }

        const int perRow = 5;
        var scoreButtons = Enumerable.Range(0, criterion.MaxScore + 1)
            .Select(value => InlineKeyboardButton.WithCallbackData(value.ToString(), $"jscore:{criterionId}:{value}"))
            .Chunk(perRow)
            .Select(row => row.ToArray())
            .ToList();
        scoreButtons.Add([InlineKeyboardButton.WithCallbackData("⬅️ Orqaga", "jback")]);

        await botClient.SendMessage(chatId, $"\"{criterion.Name}\" uchun ball tanlang:",
            replyMarkup: new InlineKeyboardMarkup(scoreButtons), cancellationToken: ct);
    }

    private async Task HandleJudgeCriterionCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !_judgeSessions.TryGetValue(chatId.Value, out var session))
        {
            return;
        }

        if (!int.TryParse(data["jcrit:".Length..], out var criterionId))
        {
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowScoreButtonsAsync(botClient, chatId.Value, session, criterionId, ct);
    }

    private async Task HandleJudgeScoreCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, string data, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !_judgeSessions.TryGetValue(chatId.Value, out var session))
        {
            return;
        }

        var parts = data["jscore:".Length..].Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var criterionId) || !int.TryParse(parts[1], out var value))
        {
            return;
        }

        try
        {
            await _scoreService.UpsertAsync(session.PresentationId, session.JudgeId, criterionId, value, ct);
            await botClient.AnswerCallbackQuery(callbackQuery.Id, $"✅ {value} ball saqlandi", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, ex.Message, showAlert: true, cancellationToken: ct);
            return;
        }

        await ShowCriteriaMenuAsync(botClient, chatId.Value, session, ct);
    }

    private async Task HandleJudgeBackCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !_judgeSessions.TryGetValue(chatId.Value, out var session))
        {
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        await ShowCriteriaMenuAsync(botClient, chatId.Value, session, ct);
    }

    /// <summary>"Yakunlash" - rather than telling the judge to press /start again, jumps straight back to
    /// their presentations list (re-fetching the Judge row for this project, since the just-cleared session
    /// only kept its Id) so scoring the next presentation takes one tap, not a fresh /start.</summary>
    private async Task HandleJudgeDoneCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null || !_judgeSessions.TryGetValue(chatId.Value, out var session))
        {
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, "✅ Yakunlandi", cancellationToken: ct);
        await botClient.SendMessage(chatId.Value, "✅ Baholash yakunlandi.", replyMarkup: JudgeMainKeyboard, cancellationToken: ct);

        var assignments = await _judgeService.GetLinkedAssignmentsByChatIdAsync(chatId.Value, ct);
        var judge = assignments.FirstOrDefault(a => a.Id == session.JudgeId);
        if (judge is null)
        {
            // The assignment itself was removed mid-session - nothing left to go back to.
            _judgeSessions.TryRemove(chatId.Value, out _);
            await botClient.SendMessage(chatId.Value, "Yana boshlash uchun /start bosing.", cancellationToken: ct);
            return;
        }

        await ShowJudgePresentationsAsync(botClient, chatId.Value, judge, ct);
    }

    private async Task HandleJudgeMessageAsync(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        // Everything in the judge flow is button-driven now - any stray text just gets redirected back to
        // whatever buttons are already on screen.
        await botClient.SendMessage(chatId, "Iltimos, tugmalardan birini tanlang.", cancellationToken: ct);
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
        else if (data.StartsWith("jproj:", StringComparison.Ordinal))
        {
            await HandleJudgeProjectCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("jpres:", StringComparison.Ordinal))
        {
            await HandleJudgePresentationCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("jcrit:", StringComparison.Ordinal))
        {
            await HandleJudgeCriterionCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data.StartsWith("jscore:", StringComparison.Ordinal))
        {
            await HandleJudgeScoreCallbackAsync(botClient, callbackQuery, data, ct);
        }
        else if (data == "jback")
        {
            await HandleJudgeBackCallbackAsync(botClient, callbackQuery, ct);
        }
        else if (data == "jdone")
        {
            await HandleJudgeDoneCallbackAsync(botClient, callbackQuery, ct);
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
