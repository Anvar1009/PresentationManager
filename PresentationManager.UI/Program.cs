using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
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

        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                // Real secrets (DB password, Telegram bot token) never live in appsettings.json (checked
                // into git) - this optional file overrides them locally and is gitignored.
                config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
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
                services.AddHostedService<PresentationBotHostedService>();

                services.AddSingleton<PresentationForm>();
                services.AddSingleton<AdminForm>();
                services.AddSingleton<AdminPanelForm>();
                services.AddSingleton<SuperAdminPanelForm>();
            })
            .Build();

        using (var db = host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            db.Database.Migrate();
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

        using var loginForm = new LoginForm(userService);
        if (loginForm.ShowDialog() == DialogResult.OK && loginForm.AuthenticatedUser is { } user)
        {
            Form mainForm = user.Role switch
            {
                UserRole.Operator => host.Services.GetRequiredService<AdminForm>(),
                UserRole.Admin => host.Services.GetRequiredService<AdminPanelForm>(),
                UserRole.SuperAdmin => host.Services.GetRequiredService<SuperAdminPanelForm>(),
                _ => throw new InvalidOperationException($"Unknown role: {user.Role}")
            };

            WinFormsApp.Run(mainForm);
        }

        // Same Task.Run reasoning as the seed call above - the WinForms message loop has already ended by
        // this point (Application.Run returned), so nothing pumps this thread's SynchronizationContext.
        Task.Run(() => host.StopAsync()).GetAwaiter().GetResult();
    }
}
