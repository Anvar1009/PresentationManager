using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;
using PresentationManager.Infrastructure.Persistence;
using PresentationManager.Infrastructure.Repositories;
using PresentationManager.Infrastructure.Services;
using PresentationManager.TelegramBot;
using PresentationManager.UI.Forms;

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
        // .exe with nothing else to hand over: uploaded files are created here on first run instead of
        // needing to sit alongside the executable (which would otherwise require copying a whole folder
        // rather than one file, and would break entirely if the exe lives somewhere read-only like Program
        // Files). The database itself now lives in PostgreSQL, not a file here - see appsettings.json.
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PresentationManager");
        Directory.CreateDirectory(appDataDir);

        // Lets the published .exe be a single standalone file with truly nothing else to hand over (e.g.
        // dropped straight on a Desktop) - its actual settings (DB connection string, Telegram bot token)
        // live here instead of next to the exe, seeded from the placeholder template embedded in the exe
        // itself the first time it runs on a given machine. Whoever deploys it then edits this one file
        // (not the exe, not anything that has to travel with it) to point at the real database/bot.
        var appDataConfigPath = Path.Combine(appDataDir, "appsettings.json");
        EnsureAppDataConfigSeeded(appDataConfigPath);

        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.AddJsonFile(appDataConfigPath, optional: true, reloadOnChange: false);

                // Real secrets (DB password, Telegram bot token) never live in appsettings.json (checked
                // into git) - this optional file, when present next to the exe, overrides the above and is
                // gitignored. Kept for local dev convenience; deployed machines use appDataConfigPath instead.
                config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

                // Re-added last (Host.CreateDefaultBuilder already adds environment variables once, earlier
                // in this same pipeline) so a ConnectionStrings__DefaultConnection env var - set directly on
                // this machine, never written to any file - wins over both JSON sources above instead of
                // being silently shadowed by whichever of them happens to also set the same key.
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                var connectionString = context.Configuration.GetConnectionString("DefaultConnection")
                    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
                services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));

                services.Configure<PresentationBotOptions>(context.Configuration.GetSection("TelegramBot"));

                services.AddSingleton<IPresentationRepository, PresentationRepository>();
                services.AddSingleton<IProjectRepository, ProjectRepository>();
                services.AddSingleton<IPresenterRepository, PresenterRepository>();
                services.AddSingleton<ISettingsRepository, SettingsRepository>();
                services.AddSingleton<IHistoryRepository, HistoryRepository>();
                services.AddSingleton<IUserRepository, UserRepository>();
                services.AddSingleton<ICriterionRepository, CriterionRepository>();
                services.AddSingleton<IJudgeRepository, JudgeRepository>();
                services.AddSingleton<IScoreRepository, ScoreRepository>();
                services.AddSingleton<IFileStorageService>(_ => new FileStorageService(Path.Combine(appDataDir, "Files")));
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
                // Registered as itself (not just via AddHostedService<T>) so ForgotPasswordForm can resolve it
                // directly to push reset codes through TrySendMessageAsync - AddHostedService<T> alone only
                // registers T as IHostedService, which isn't resolvable by its own concrete type.
                services.AddSingleton<PresentationBotHostedService>();
                services.AddHostedService(sp => sp.GetRequiredService<PresentationBotHostedService>());

                services.AddSingleton<PresentationForm>();
                services.AddSingleton<AdminForm>();
                services.AddSingleton<AdminPanelForm>();
                services.AddSingleton<SuperAdminPanelForm>();
            })
            .Build();

        // A bad/placeholder connection string (e.g. the freshly-seeded appDataConfigPath template, still
        // carrying "Password=CHANGE_ME", on a machine's very first launch) throws here - before any Form
        // has ever been shown, on a WinExe subsystem with no console attached. Left uncaught, that's a
        // silent crash: the exe flashes and disappears with zero visible explanation, on this machine's
        // first run *and* on every future one until someone happens to check the Windows Event Log. A
        // message box naming the actual file to go edit turns that into something self-service-fixable.
        try
        {
            using var db = host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ma'lumotlar bazasiga ulanib bo'lmadi.\n\nSozlamalar fayli:\n{appDataConfigPath}\n\nXatolik: {ex.Message}",
                "Ulanishda xatolik",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Starts registered IHostedServices - notably PresentationBotHostedService, which otherwise would
        // never run (this host was previously only ever used as a DI container, never actually started).
        host.Start();

        var userService = host.Services.GetRequiredService<UserService>();
        // Task.Run, not a direct blocking await: the main thread's SynchronizationContext is already the
        // WindowsFormsSynchronizationContext set above, but no message loop is pumping it yet (Application.Run
        // hasn't started, no ShowDialog is open) - awaiting this directly would capture that context and
        // deadlock the moment any inner await tries to post its continuation back to a queue nothing is
        // draining. Running it on the thread pool sidesteps the captured context entirely.
        Task.Run(() => userService.EnsureDefaultSuperAdminAsync()).GetAwaiter().GetResult();

        using var loginForm = new LoginForm(
            userService,
            host.Services.GetRequiredService<PasswordResetService>(),
            host.Services.GetRequiredService<PresentationBotHostedService>());
        if (loginForm.ShowDialog() == DialogResult.OK && loginForm.AuthenticatedUser is { } user)
        {
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

        // Same Task.Run reasoning as the seed call above - the WinForms message loop has already ended by
        // this point (Application.Run returned), so nothing pumps this thread's SynchronizationContext.
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
}
