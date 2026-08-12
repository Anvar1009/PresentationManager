using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PresentationManager.API.Services;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Infrastructure.Persistence;
using PresentationManager.Infrastructure.Repositories;
using PresentationManager.Infrastructure.Services;
using PresentationManager.TelegramBot;

var builder = WebApplication.CreateBuilder(args);

// No-op when run interactively (e.g. `dotnet run` during local testing) - only takes effect when actually
// launched by systemd. Same reasoning as PresentationManager.BotService/Program.cs.
builder.Host.UseSystemd();

// Same appsettings.json -> appsettings.Local.json (gitignored) -> env vars layering as BotService/UI: real
// secrets (DB password, Jwt:Secret) never live in the committed appsettings.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IPresentationRepository, PresentationRepository>();
builder.Services.AddSingleton<IProjectRepository, ProjectRepository>();
builder.Services.AddSingleton<IPresenterRepository, PresenterRepository>();
builder.Services.AddSingleton<ISettingsRepository, SettingsRepository>();
builder.Services.AddSingleton<IHistoryRepository, HistoryRepository>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();
builder.Services.AddSingleton<ICriterionRepository, CriterionRepository>();
builder.Services.AddSingleton<IJudgeRepository, JudgeRepository>();
builder.Services.AddSingleton<IScoreRepository, ScoreRepository>();
builder.Services.AddSingleton<IFileStorageService>(_ => new FileStorageService(ResolveStorageRoot(builder.Configuration)));

builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<CriterionService>();
builder.Services.AddSingleton<JudgeService>();
builder.Services.AddSingleton<ScoreService>();
builder.Services.AddSingleton<AdminLinkService>();
builder.Services.AddSingleton<PresentationQueueService>();

// The bot token itself lives only here (and in PresentationManager.BotService) now - PresentationManager.UI
// no longer constructs a TelegramNotifier at all, it only ever calls the text-only ITelegramSender contract
// over HTTP (see NotificationsController). JudgesController keeps using the concrete TelegramNotifier
// directly (not the ITelegramSender registration below) since the judge-assignment push needs the
// ReplyMarkup keyboard ITelegramSender deliberately doesn't expose.
builder.Services.Configure<PresentationBotOptions>(builder.Configuration.GetSection("TelegramBot"));
builder.Services.AddSingleton<TelegramNotifier>();
builder.Services.AddSingleton<ITelegramSender>(sp => sp.GetRequiredService<TelegramNotifier>());

var jwtSecret = builder.Configuration["Jwt:Secret"] is { Length: > 0 } secret
    ? secret
    : throw new InvalidOperationException("Jwt:Secret is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "PresentationManager.API";
var jwtExpiryHours = builder.Configuration.GetValue("Jwt:ExpiryHours", 12);

builder.Services.AddSingleton(new JwtTokenService(jwtSecret, jwtIssuer, TimeSpan.FromHours(jwtExpiryHours)));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

// A bad/placeholder connection string throws here, before the app starts accepting requests - visible via
// `systemctl status`/journald rather than failing silently on the first real request.
using (var scope = app.Services.CreateScope())
{
    using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    db.Database.Migrate();

    // Bootstraps the very first login account on an empty database. Moved here (server-side) rather than
    // staying in PresentationManager.UI/Program.cs where it used to run: UsersController's POST endpoint is
    // now SuperAdmin-only, so a not-yet-logged-in desktop client has no token to call it with - only this
    // process, which already has a direct IUserRepository, can bootstrap without hitting that chicken-and-egg
    // problem. Also avoids every desktop racing to create a duplicate "superadmin" the moment a shared server
    // has zero users, instead of just this one process doing it once at startup.
    await scope.ServiceProvider.GetRequiredService<UserService>().EnsureDefaultSuperAdminAsync();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Unauthenticated on purpose - PresentationManager.UI pings this before showing LoginForm to fail fast
// with a friendly message when the server is unreachable, the same way a bad DB connection string used to
// fail fast before this app's own database ever moved behind an API.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Resolves where uploaded presentation files are stored - identical convention to
/// PresentationManager.BotService/Program.cs's ResolveStorageRoot (a configured "Storage:RootPath", or a
/// local "Files" folder next to this process when unset). Now that PresentationManager.UI talks to files
/// only through this API's own FilesController (see Phase 3), this can simply be a local folder on the
/// server - no shared network path needed.</summary>
static string ResolveStorageRoot(IConfiguration config) =>
    config["Storage:RootPath"] is { Length: > 0 } configured
        ? configured
        : Path.Combine(AppContext.BaseDirectory, "Files");
