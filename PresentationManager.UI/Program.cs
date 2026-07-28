using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Infrastructure.Persistence;
using PresentationManager.Infrastructure.Repositories;
using PresentationManager.Infrastructure.Services;
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

        var dbPath = Path.Combine(AppContext.BaseDirectory, "presentationmanager.db");

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

                services.AddSingleton<IPresentationRepository, PresentationRepository>();
                services.AddSingleton<ISettingsRepository, SettingsRepository>();
                services.AddSingleton<IHistoryRepository, HistoryRepository>();
                services.AddSingleton<IFileStorageService>(_ => new FileStorageService());
                services.AddSingleton<IAlarmSoundService, AlarmSoundService>();

                services.AddSingleton<TimerEngine>();
                services.AddSingleton<PresentationSessionController>();
                services.AddSingleton<PresentationQueueService>();

                services.AddSingleton<PresentationForm>();
                services.AddSingleton<AdminForm>();
            })
            .Build();

        using (var db = host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            db.Database.Migrate();
        }

        var adminForm = host.Services.GetRequiredService<AdminForm>();
        WinFormsApp.Run(adminForm);
    }
}
