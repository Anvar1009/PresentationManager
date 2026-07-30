using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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

/// <summary>Two Telegram-only roles live here, routed purely by who's already known to the system when a
/// chat says /start: a <b>Presenter</b> uploads presentations into a project's queue (see
/// <see cref="HandleDocumentAsync"/>), a <b>Judge</b> scores presentations against a project's dynamic
/// criteria (see <see cref="ShowJudgePresentationsAsync"/> onward). Both start from the same one-time
/// registration (full name, then sharing a Telegram contact) and land as a Presenter — becoming a Judge only
/// happens afterward, when Admin picks that already-registered person from the Admin panel's "Hakamlar"
/// dialog (<c>JudgeService.AssignAsync</c>); this class subscribes to <c>JudgeService.JudgeAssigned</c> to
/// push that person a notification the moment it happens (<see cref="OnJudgeAssignedAsync"/>). Runs embedded
/// in the same process as the WinForms admin app (started via <c>Program.cs</c>'s host), reusing its
/// existing service singletons - a submitted file lands in the database and managed file storage exactly
/// the same way <c>AdminForm.OnAddClick</c> does.</summary>
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
    private static readonly ReplyKeyboardMarkup JudgeMainKeyboard = new(
        new[] { new KeyboardButton("📋 Loyihalar") })
    {
        ResizeKeyboard = true
    };

    private const string JudgeProjectsButtonText = "📋 Loyihalar";

    private readonly PresentationBotOptions _options;
    private readonly ProjectService _projectService;
    private readonly PresentationQueueService _queueService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IPresenterRepository _presenterRepository;
    private readonly JudgeService _judgeService;
    private readonly ScoreService _scoreService;
    private readonly CriterionService _criterionService;

    /// <summary>Set once <see cref="ExecuteAsync"/> actually starts polling (i.e. a token is configured) -
    /// null otherwise, which <see cref="OnJudgeAssignedAsync"/> treats as "bot is off, nothing to push".</summary>
    private ITelegramBotClient? _botClient;

    /// <summary>Presenter upload flow state, per chat.</summary>
    private readonly ConcurrentDictionary<long, ChatSession> _sessions = new();

    /// <summary>Judge scoring flow state, per chat — separate from <see cref="_sessions"/> since a chat is
    /// only ever in one flow at a time but the two shapes don't overlap.</summary>
    private readonly ConcurrentDictionary<long, JudgeSession> _judgeSessions = new();

    public PresentationBotHostedService(
        IOptions<PresentationBotOptions> options,
        ProjectService projectService,
        PresentationQueueService queueService,
        ISettingsRepository settingsRepository,
        IPresenterRepository presenterRepository,
        JudgeService judgeService,
        ScoreService scoreService,
        CriterionService criterionService)
    {
        _options = options.Value;
        _projectService = projectService;
        _queueService = queueService;
        _settingsRepository = settingsRepository;
        _presenterRepository = presenterRepository;
        _judgeService = judgeService;
        _scoreService = scoreService;
        _criterionService = criterionService;

        _judgeService.JudgeAssigned += judge => _ = OnJudgeAssignedAsync(judge);
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
        _botClient = botClient;
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

        if (message.Text is "/start" or "/cancel" or JudgeProjectsButtonText)
        {
            _sessions.TryRemove(chatId, out _);
            _judgeSessions.TryRemove(chatId, out _);
            await BeginAsync(botClient, chatId, ct);
            return;
        }

        if (_judgeSessions.ContainsKey(chatId))
        {
            await HandleJudgeMessageAsync(botClient, chatId, ct);
            return;
        }

        if (!_sessions.TryGetValue(chatId, out var session))
        {
            await BeginAsync(botClient, chatId, ct);
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

    /// <summary>Entry point for both the very first message from a chat and every subsequent /start. Judges
    /// take priority (someone Admin already assigned always means judge, even if that same person also
    /// uploads presentations elsewhere) - then already-registered presenters skip straight to project
    /// selection - brand new chats go through the one-time full name + contact-share registration, which
    /// always lands them as a Presenter (becoming a Judge only happens afterward, when Admin assigns them
    /// from the Admin panel - see <see cref="OnJudgeAssignedAsync"/>).</summary>
    private async Task BeginAsync(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
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

    /// <summary>Fired by <c>JudgeService.AssignAsync</c> the moment Admin assigns an already-registered
    /// person as a judge - pushes them a notification immediately instead of leaving them to discover it
    /// only if/when they happen to press /start again on their own.</summary>
    private async Task OnJudgeAssignedAsync(Judge judge)
    {
        if (_botClient is not { } botClient || judge.TelegramChatId is not { } chatId)
        {
            return;
        }

        try
        {
            var projects = await _projectService.GetAllAsync();
            var projectName = projects.FirstOrDefault(p => p.Id == judge.ProjectId)?.Name ?? "loyiha";

            await botClient.SendMessage(chatId,
                $"🎉 Tabriklaymiz! Siz \"{projectName}\" loyihasi uchun hakam etib tayinlandingiz.\nPastdagi \"📋 Loyihalar\" tugmasi orqali istalgan vaqt boshlashingiz mumkin.",
                replyMarkup: JudgeMainKeyboard);
        }
        catch (Exception ex)
        {
            // Best-effort push notification - the assignment itself already succeeded regardless of whether
            // this message manages to reach them (e.g. they blocked the bot).
            Debug.WriteLine($"Failed to notify newly-assigned judge (chat {chatId}): {ex}");
        }
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
                session.PresenterId, ct);

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
    }
}
