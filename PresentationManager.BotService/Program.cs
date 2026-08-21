using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Infrastructure.Persistence;
using PresentationManager.Infrastructure.Repositories;
using PresentationManager.Infrastructure.Services;
using PresentationManager.TelegramBot;
using Serilog;

// Bootstrap logger: catches anything that fails before the real, config-driven logger below is built (a
// bad Serilog config section itself, etc). Replaced by the "Serilog" appsettings.json section - see
// UseSerilog below - once configuration is available.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("PresentationManager.BotService ishga tushmoqda...");

var builder = Host.CreateDefaultBuilder(args)
    // No-ops when run interactively (e.g. during local testing) - only takes effect when actually launched
    // by systemd, wiring this process's logging/lifecycle into the service manager (journald logs, proper
    // SIGTERM-triggered shutdown on `systemctl stop`, notifying systemd once startup - including the
    // Migrate() call below - has actually finished).
    .UseSystemd()
    .UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext())
    .ConfigureAppConfiguration(config =>
    {
        // Real secrets (DB password, Telegram bot token) never live in the committed appsettings.json -
        // this optional, gitignored file overrides it once placed next to the installed exe. Same layering
        // convention as PresentationManager.UI/Program.cs.
        config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        var connectionString = context.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.Configure<PresentationBotOptions>(context.Configuration.GetSection("TelegramBot"));

        // Only what PresentationBotHostedService's three Telegram-side flows (Presenter uploads, Judge
        // scoring, linked-Admin mirror) actually need - IHistoryRepository/PresentationSessionController/
        // TimerEngine are a WinForms live-session concern this worker has no equivalent of.
        services.AddSingleton<IPresentationRepository, PresentationRepository>();
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<IPresenterRepository, PresenterRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<ICriterionRepository, CriterionRepository>();
        services.AddSingleton<IJudgeRepository, JudgeRepository>();
        services.AddSingleton<IScoreRepository, ScoreRepository>();
        services.AddSingleton<IPresenterProjectAssignmentRepository, PresenterProjectAssignmentRepository>();
        services.AddSingleton<IFileStorageService>(sp =>
            new FileStorageService(ResolveStorageRoot(context.Configuration), sp.GetRequiredService<ILogger<FileStorageService>>()));

        services.AddSingleton<ProjectService>();
        services.AddSingleton<UserService>();
        services.AddSingleton<CriterionService>();
        services.AddSingleton<JudgeService>();
        services.AddSingleton<ScoreService>();
        services.AddSingleton<PresenterAssignmentService>();
        services.AddSingleton<AdminLinkService>();
        services.AddSingleton<PresentationQueueService>();

        services.AddSingleton<TelegramNotifier>();
        services.AddSingleton<JudgeAssignmentNotifier>();

        services.AddSingleton<PresentationBotHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<PresentationBotHostedService>());
    });

using var host = builder.Build();

// No WinForms MessageBox to show here (this runs headless, as a Windows Service) - a bad connection string
// or unreachable DB throws and fails the service to start, visible via `sc query`/the Windows Event Log,
// which is the correct failure mode for a service rather than a silently-hanging process.
using (var db = host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
{
    db.Database.Migrate();
}

// Eagerly resolved so its constructor's JudgeService.JudgeAssigned subscription is live even though nothing
// else in this process resolves it by injection - covers a linked Admin assigning a judge via the bot's own
// Telegram-side admin mirror (a desktop Admin's assignment is covered by the UI process's own copy instead).
host.Services.GetRequiredService<JudgeAssignmentNotifier>();

await host.RunAsync();
}
catch (Exception ex)
{
    // Mirrors PresentationManager.API/Program.cs's own fatal-startup handling - a bad connection string or
    // unreachable DB now lands in the log file (see appsettings.json's "Serilog" section) in addition to
    // failing the systemd unit visibly via `systemctl status`/journald.
    Log.Fatal(ex, "PresentationManager.BotService ishga tushirishda halokatli xatolik yuz berdi.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Same resolution rule as PresentationManager.UI/Program.cs's ResolveStorageRoot - both processes
/// must be pointed at the same "Storage:RootPath" for a shared deployment to work. No AppData concept exists
/// for a service account, so the fallback here is a local "Files" folder next to the exe instead - in
/// practice a real deployment should always set this explicitly, since a service account's local disk isn't
/// somewhere operators can browse.</summary>
static string ResolveStorageRoot(IConfiguration config) =>
    config["Storage:RootPath"] is { Length: > 0 } configured
        ? configured
        : Path.Combine(AppContext.BaseDirectory, "Files");
