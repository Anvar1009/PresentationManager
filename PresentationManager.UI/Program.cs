using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
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
                services.AddSingleton<IFileStorageService>(_ => new FileStorageService(Path.Combine(appDataDir, "Files")));
                services.AddSingleton<IAlarmSoundService, AlarmSoundService>();

                services.AddSingleton<TimerEngine>();
                services.AddSingleton<PresentationSessionController>();
                services.AddSingleton<PresentationQueueService>();
                services.AddSingleton<ProjectService>();
                services.AddHostedService<PresentationBotHostedService>();

                services.AddSingleton<PresentationForm>();
                services.AddSingleton<AdminForm>();
            })
            .Build();

        using (var db = host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            db.Database.Migrate();
        }

        // Starts registered IHostedServices - notably PresentationBotHostedService, which otherwise would
        // never run (this host was previously only ever used as a DI container, never actually started).
        host.Start();

        var adminForm = host.Services.GetRequiredService<AdminForm>();
        WinFormsApp.Run(adminForm);

        host.StopAsync().GetAwaiter().GetResult();
    }
}
