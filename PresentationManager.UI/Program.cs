using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PresentationManager.ApiClient;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.TelegramBot;
using PresentationManager.UI.Forms;
using PresentationManager.UI.Services;

namespace PresentationManager.UI;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Installed explicitly (rather than relying on it being auto-installed by the first Control) so
        // that DI singletons resolved before any Form is constructed — e.g. TimerEngine — already see a
        // valid SynchronizationContext.Current and can marshal ticks back to this thread reliably.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        // Per-user AppData, not AppContext.BaseDirectory — so the published app is a single standalone
        // .exe with nothing else to hand over: the settings file and the file-download cache are created
        // here on first run instead of needing to sit alongside the executable (which would otherwise
        // require copying a whole folder rather than one file, and would break entirely if the exe lives
        // somewhere read-only like Program Files). Everything this app used to keep locally (the database,
        // uploaded files) now lives behind PresentationManager.API instead - see appsettings.json.
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PresentationManager");
        Directory.CreateDirectory(appDataDir);

        // Lets the published .exe be a single standalone file with truly nothing else to hand over (e.g.
        // dropped straight on a Desktop) - its actual settings (API base URL) live here instead of next to
        // the exe, seeded from the placeholder template embedded in the exe itself the first time it runs
        // on a given machine. Whoever deploys it then edits this one file (not the exe, not anything that
        // has to travel with it) to point at the real server.
        var appDataConfigPath = Path.Combine(appDataDir, "appsettings.json");
        EnsureAppDataConfigSeeded(appDataConfigPath);

        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.AddJsonFile(appDataConfigPath, optional: true, reloadOnChange: false);

                // Real secrets (Telegram bot token, for whatever in this process still needs one) never
                // live in appsettings.json (checked into git) - this optional file, when present next to
                // the exe, overrides the above and is gitignored. Kept for local dev convenience; deployed
                // machines use appDataConfigPath instead.
                config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

                // Re-added last (Host.CreateDefaultBuilder already adds environment variables once, earlier
                // in this same pipeline) so an Api__BaseUrl env var - set directly on this machine, never
                // written to any file - wins over both JSON sources above instead of being silently
                // shadowed by whichever of them happens to also set the same key.
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                var apiBaseUrl = context.Configuration["Api:BaseUrl"] is { Length: > 0 } configuredBaseUrl
                    ? configuredBaseUrl
                    : throw new InvalidOperationException("Api:BaseUrl is not configured.");

                services.Configure<PresentationBotOptions>(context.Configuration.GetSection("TelegramBot"));

                services.AddSingleton<AuthSession>();
                services.AddTransient<JwtAuthHandler>();

                // One typed HttpClient per PresentationManager.Application.Interfaces contract, each backed
                // by PresentationManager.ApiClient's HTTP implementation instead of the EF Core/Infrastructure
                // one this app used to talk to Postgres directly with - every Application service above them
                // (ProjectService, UserService, PresentationQueueService, ...) keeps working completely
                // unchanged against the same interfaces.
                AddApiHttpClient<IPresentationRepository, HttpPresentationRepository>(services, apiBaseUrl);
                AddApiHttpClient<IProjectRepository, HttpProjectRepository>(services, apiBaseUrl);
                AddApiHttpClient<IPresenterRepository, HttpPresenterRepository>(services, apiBaseUrl);
                AddApiHttpClient<ISettingsRepository, HttpSettingsRepository>(services, apiBaseUrl);
                AddApiHttpClient<IHistoryRepository, HttpHistoryRepository>(services, apiBaseUrl);
                AddApiHttpClient<IUserRepository, HttpUserRepository>(services, apiBaseUrl);
                AddApiHttpClient<ICriterionRepository, HttpCriterionRepository>(services, apiBaseUrl);
                AddApiHttpClient<IJudgeRepository, HttpJudgeRepository>(services, apiBaseUrl);
                AddApiHttpClient<IScoreRepository, HttpScoreRepository>(services, apiBaseUrl);
                AddApiHttpClient<IFileStorageService, HttpFileStorageService>(services, apiBaseUrl);
                AddApiHttpClient<IAuthService, HttpAuthService>(services, apiBaseUrl);
                // Text-only Telegram relay (password reset codes, SuperAdmin-issued credentials) - the bot
                // token itself now lives only on the server, see PresentationManager.API.Controllers.
                // NotificationsController. Judge-assignment pushes moved server-side entirely (into
                // JudgesController), so there is no client-side equivalent of the old JudgeAssignmentNotifier
                // to register anymore.
                AddApiHttpClient<ITelegramSender, HttpTelegramSender>(services, apiBaseUrl);

                // Built (not connected) here; AdminForm connects it once AuthSession.Token is actually set
                // (after login) - see OrderHubClient's own remarks. Must resolve the same AuthSession
                // singleton LoginForm populates (line ~73), not a fresh instance. Only ever a *listener*
                // here - the "Tartib operatori" role that *triggers* a randomize moved to the web platform
                // (PresentationManager.API's OrderController), so this desktop process has no client that
                // calls the randomize endpoint itself anymore.
                services.AddSingleton(sp => new OrderHubClient(apiBaseUrl, sp.GetRequiredService<AuthSession>()));

                // Purely local audio playback - never had anything to do with the database, so it stays a
                // plain local implementation (see PresentationManager.UI.Services.AlarmSoundService, moved
                // here from PresentationManager.Infrastructure).
                services.AddSingleton<IAlarmSoundService, AlarmSoundService>();

                services.AddSingleton<TimerEngine>();
                services.AddSingleton<PresentationSessionController>();
                services.AddSingleton<PresentationQueueService>();
                services.AddSingleton<ProjectService>();
                services.AddSingleton<UserService>();
                services.AddSingleton<CriterionService>();
                services.AddSingleton<JudgeService>();
                services.AddSingleton<ScoreService>();
                services.AddSingleton<AdminLinkService>();
                services.AddSingleton<PasswordResetService>();

                services.AddSingleton<PresentationForm>();
                services.AddSingleton<AdminForm>();
                services.AddSingleton<AdminPanelForm>();
                services.AddSingleton<SuperAdminPanelForm>();
            })
            .Build();

        var apiBaseUrlForHealthCheck = host.Services.GetRequiredService<IConfiguration>()["Api:BaseUrl"]!;

        // An unreachable server (bad/placeholder Api:BaseUrl, server down, no network) fails here - before
        // any Form has ever been shown, on a WinExe subsystem with no console attached. Left uncaught,
        // that's a silent crash: the exe flashes and disappears with zero visible explanation, on this
        // machine's first run *and* on every future one until someone happens to notice. A message box
        // naming the actual file to go edit turns that into something self-service-fixable - same UX this
        // app already had for a bad database connection string, before the database moved behind the API.
        try
        {
            using var healthCheckClient = new HttpClient { BaseAddress = new Uri(apiBaseUrlForHealthCheck) };
            // Task.Run, not a direct blocking await: the WindowsFormsSynchronizationContext set above has
            // nothing pumping it yet (Application.Run hasn't started) - awaiting directly would deadlock the
            // moment this inner await tries to post its continuation back to a queue nothing is draining.
            Task.Run(() => healthCheckClient.GetAsync("health")).GetAwaiter().GetResult().EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Serverga ulanib bo'lmadi.\n\nSozlamalar fayli:\n{appDataConfigPath}\n\nAPI manzili:\n{apiBaseUrlForHealthCheck}\n\nXatolik: {ex.Message}",
                "Ulanishda xatolik",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // No IHostedService is registered in this process anymore (the bot's receive loop moved to
        // PresentationManager.BotService) - Start()/StopAsync() are kept as harmless no-ops rather than
        // removed, so this host still behaves correctly if a future hosted service is ever added here.
        host.Start();

        // Surfaces a mid-session token expiry/revocation as one message instead of a confusing wall of 401
        // errors the next time any Form happens to call the API - full re-login without restarting isn't
        // wired up yet, so this is an honest "please restart" rather than a silent failure.
        host.Services.GetRequiredService<AuthSession>().SessionExpired += () =>
            MessageBox.Show(
                "Sessiya muddati tugadi. Iltimos, dasturni qayta ishga tushiring va qaytadan kiring.",
                "Sessiya tugadi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

        // Bootstrapping the very first ("superadmin") account now happens server-side, in
        // PresentationManager.API/Program.cs - a not-yet-logged-in desktop client has no token to call the
        // (now SuperAdmin-only) create-user endpoint with, so only the server itself can still do this.
        using var loginForm = new LoginForm(
            host.Services.GetRequiredService<IAuthService>(),
            host.Services.GetRequiredService<UserService>(),
            host.Services.GetRequiredService<PasswordResetService>(),
            host.Services.GetRequiredService<ITelegramSender>());
        if (loginForm.ShowDialog() == DialogResult.OK && loginForm.AuthenticatedUser is { } user)
        {
            // Judge and OrderOperator are both web-only (see UserRole.Judge's doc comment - OrderOperator
            // moved there too, so its "Tartib operatori" dashboard is PresentationManager.API's
            // OrderController, not a desktop form anymore) - the desktop LoginForm has no way to know that
            // ahead of a successful password check, so this is caught here instead of inside the switch
            // below, with a friendly explanation rather than the "Unknown role" exception every other
            // truly-unexpected value still falls into.
            if (user.Role is UserRole.Judge or UserRole.OrderOperator)
            {
                MessageBox.Show(
                    "Bu hisob faqat veb-sahifa orqali kiradi, desktop dasturga emas.",
                    "Kirish rad etildi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                Task.Run(() => host.StopAsync()).GetAwaiter().GetResult();
                return;
            }

            Form mainForm = user.Role switch
            {
                // All three role forms are DI singletons built before login happens, so none of them has a
                // way to receive the logged-in user through its constructor - SetCurrentUser wires it in
                // here instead, before the form ever runs (AdminForm needs it for its own "Botga ulash" in
                // Sozlamalar; AdminPanelForm needs it to scope "Loyihalar" to whichever Admin this is; all
                // three need it for the profile-info/Chiqish menu built by UserMenuHelper).
                UserRole.Operator => WithCurrentOperatorUser(host.Services.GetRequiredService<AdminForm>(), user),
                UserRole.Admin => WithCurrentAdminUser(host.Services.GetRequiredService<AdminPanelForm>(), user),
                UserRole.SuperAdmin => WithCurrentSuperAdminUser(host.Services.GetRequiredService<SuperAdminPanelForm>(), user),
                _ => throw new InvalidOperationException($"Unknown role: {user.Role}")
            };

            WinFormsApp.Run(mainForm);
        }

        // Local functions can't be overloaded by parameter type the way regular methods can - hence the
        // distinct names, even though all three bodies are identical modulo the form type.
        static AdminForm WithCurrentOperatorUser(AdminForm form, User user)
        {
            form.SetCurrentUser(user);
            return form;
        }

        static AdminPanelForm WithCurrentAdminUser(AdminPanelForm form, User user)
        {
            form.SetCurrentUser(user);
            return form;
        }

        static SuperAdminPanelForm WithCurrentSuperAdminUser(SuperAdminPanelForm form, User user)
        {
            form.SetCurrentUser(user);
            return form;
        }

        // Same Task.Run reasoning as the health-check call above - the WinForms message loop has already
        // ended by this point (Application.Run returned), so nothing pumps this thread's SynchronizationContext.
        Task.Run(() => host.StopAsync()).GetAwaiter().GetResult();
    }

    /// <summary>Writes the placeholder config template (embedded in the exe as a resource, so the published
    /// app needs no companion file to do this) to <paramref name="appDataConfigPath"/> the first time this
    /// app runs on a given machine - never overwrites an existing file, so whatever real values get filled
    /// in there afterward survive every future launch/update.</summary>
    private static void EnsureAppDataConfigSeeded(string appDataConfigPath)
    {
        if (File.Exists(appDataConfigPath))
        {
            return;
        }

        using var resourceStream = typeof(Program).Assembly.GetManifestResourceStream("PresentationManager.UI.appsettings.json")
            ?? throw new InvalidOperationException("Embedded default appsettings.json resource is missing.");
        using var fileStream = File.Create(appDataConfigPath);
        resourceStream.CopyTo(fileStream);
    }

    /// <summary>Registers a typed HttpClient (base address = the API, JwtAuthHandler attaching the bearer
    /// token from AuthSession on every request) for <typeparamref name="TImplementation"/> against
    /// <typeparamref name="TService"/> - one call per PresentationManager.ApiClient class, keeping the
    /// registration boilerplate in ConfigureServices above to a single line each.</summary>
    private static void AddApiHttpClient<TService, TImplementation>(IServiceCollection services, string apiBaseUrl)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddHttpClient<TService, TImplementation>(client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
        }).AddHttpMessageHandler<JwtAuthHandler>();
    }
}
