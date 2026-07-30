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

/// <summary>Lets presenters upload their own presentation straight into a project's queue over Telegram,
/// instead of the operator having to add every file by hand in <c>AdminForm</c>. Runs embedded in the same
/// process as the WinForms admin app (started via <c>Program.cs</c>'s host), reusing its existing
/// <see cref="ProjectService"/>/<see cref="PresentationQueueService"/> singletons - a submitted file lands
/// in the database and managed file storage exactly the same way <c>AdminForm.OnAddClick</c> does. A
/// presenter's identity (<see cref="IPresenterRepository"/>) is captured once via a short registration
/// (full name, then sharing their Telegram contact) so every upload after the first only needs a project
/// and a title, not their name again.</summary>
public sealed class PresentationBotHostedService : BackgroundService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ppt", ".pptx", ".pdf" };

    private static readonly ReplyKeyboardMarkup ContactRequestKeyboard = new(
        new[] { new KeyboardButton("📱 Kontaktni ulashish") { RequestContact = true } })
    {
        ResizeKeyboard = true,
        OneTimeKeyboard = true
    };

    private readonly PresentationBotOptions _options;
    private readonly ProjectService _projectService;
    private readonly PresentationQueueService _queueService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IPresenterRepository _presenterRepository;

    /// <summary>Per-chat conversation progress. Telegram has no notion of a "session" of its own, so each
    /// chat's place in (one-time) registration and, per upload, project -> title -> file is tracked here for
    /// as long as the app runs.</summary>
    private readonly ConcurrentDictionary<long, ChatSession> _sessions = new();

    public PresentationBotHostedService(
        IOptions<PresentationBotOptions> options,
        ProjectService projectService,
        PresentationQueueService queueService,
        ISettingsRepository settingsRepository,
        IPresenterRepository presenterRepository)
    {
        _options = options.Value;
        _projectService = projectService;
        _queueService = queueService;
        _settingsRepository = settingsRepository;
        _presenterRepository = presenterRepository;
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

        if (message.Text is "/start" or "/cancel")
        {
            _sessions.TryRemove(chatId, out _);
            await BeginAsync(botClient, chatId, ct);
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

    /// <summary>Entry point for both the very first message from a chat and every subsequent /start —
    /// already-registered presenters skip straight to project selection, new ones go through the one-time
    /// full name + contact-share registration first.</summary>
    private async Task BeginAsync(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        var presenter = await _presenterRepository.GetByTelegramChatIdAsync(chatId, ct);
        if (presenter is null)
        {
            _sessions[chatId] = new ChatSession { Step = SessionStep.AwaitingRegistrationFullName };
            await botClient.SendMessage(chatId,
                "Xush kelibsiz! Avval ro'yxatdan o'tamiz - bu faqat bir marta so'raladi.\nIsm-familyangizni kiriting:",
                cancellationToken: ct);
            return;
        }

        await ShowProjectListAsync(botClient, chatId, presenter.FullName, ct);
    }

    private async Task CompleteRegistrationAsync(
        ITelegramBotClient botClient, long chatId, ChatSession session, Contact contact, string? telegramUsername, CancellationToken ct)
    {
        await _presenterRepository.AddAsync(new Presenter
        {
            TelegramChatId = chatId,
            FullName = session.FullName,
            PhoneNumber = contact.PhoneNumber,
            TelegramUsername = telegramUsername
        }, ct);

        await botClient.SendMessage(chatId, "✅ Ro'yxatdan o'tish yakunlandi!", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
        await ShowProjectListAsync(botClient, chatId, session.FullName, ct);
    }

    private async Task ShowProjectListAsync(ITelegramBotClient botClient, long chatId, string fullName, CancellationToken ct)
    {
        var projects = await _projectService.GetAllAsync(ct);
        if (projects.Count == 0)
        {
            await botClient.SendMessage(chatId, "Hozircha loyihalar mavjud emas. Keyinroq urinib ko'ring.", cancellationToken: ct);
            return;
        }

        _sessions[chatId] = new ChatSession { Step = SessionStep.AwaitingProject, FullName = fullName };

        var buttons = projects
            .Select(p => new[] { InlineKeyboardButton.WithCallbackData(p.Name, $"project:{p.Id}") })
            .ToArray();

        await botClient.SendMessage(chatId, "Taqdimot yuborish uchun loyihani tanlang:",
            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        var data = callbackQuery.Data;
        if (chatId is null || data is null || !data.StartsWith("project:", StringComparison.Ordinal)
            || !int.TryParse(data["project:".Length..], out var projectId))
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
                settings.DefaultPresentationTimeSeconds, settings.DefaultDiscussionTimeSeconds, ct);

            await botClient.SendMessage(chatId,
                $"✅ Taqdimotingiz \"{session.ProjectName}\" loyihasiga qabul qilindi. Yana yuborish uchun /start bosing.",
                cancellationToken: ct);
        }
        finally
        {
            File.Delete(tempFilePath);
            _sessions.TryRemove(chatId, out _);
        }
    }
}
