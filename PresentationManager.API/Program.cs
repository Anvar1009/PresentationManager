using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PresentationManager.API.Hubs;
using PresentationManager.API.Services;
using PresentationManager.Application.Interfaces;
using PresentationManager.Application.Services;
using PresentationManager.Infrastructure.Persistence;
using PresentationManager.Infrastructure.Repositories;
using PresentationManager.Infrastructure.Services;
using PresentationManager.TelegramBot;
using Serilog;

// Bootstrap logger: catches anything that fails before the real, config-driven logger below is built
// (e.g. a bad Serilog config section itself) - see the Log.Logger reassignment inside UseSerilog further
// down, which is what actually serves the app once builder.Build() runs.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("PresentationManager.API ishga tushmoqda...");

var builder = WebApplication.CreateBuilder(args);

// No-op when run interactively (e.g. `dotnet run` during local testing) - only takes effect when actually
// launched by systemd. Same reasoning as PresentationManager.BotService/Program.cs.
builder.Host.UseSystemd();

// Replaces the bootstrap logger above with the real, appsettings.json-driven one ("Serilog" section - see
// PresentationManager.API/appsettings.json) once configuration is available. ReadFrom.Services lets any
// registered Serilog enricher/sink resolve DI services; not used yet but keeps this future-proof.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

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
builder.Services.AddSingleton<IPresenterProjectAssignmentRepository, PresenterProjectAssignmentRepository>();
builder.Services.AddSingleton<IFileStorageService>(sp =>
    new FileStorageService(ResolveStorageRoot(builder.Configuration), sp.GetRequiredService<ILogger<FileStorageService>>()));

builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<CriterionService>();
builder.Services.AddSingleton<JudgeService>();
builder.Services.AddSingleton<ScoreService>();
builder.Services.AddSingleton<AdminLinkService>();
builder.Services.AddSingleton<PresentationQueueService>();
builder.Services.AddSingleton<PresenterAssignmentService>();

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
    })
    // Second, non-default scheme - only the Judge web platform's Account/Judge controllers and
    // PresentationOrderHub opt into it explicitly (see their own [Authorize(AuthenticationSchemes = ...)]).
    // Every existing [ApiController] endpoint is untouched: no AuthenticationSchemes means "the default
    // scheme", which stays JwtBearer.
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.Name = "PresentationManager.Judge";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

// Without this, ASP.NET Core has to guess where it's safe to persist the Data Protection key ring (what
// protects the cookie-auth ticket AND every antiforgery token this app issues) - under a dedicated systemd
// service user with no writable/stable home directory, that guess can silently fall back to an in-memory-only
// key that's thrown away and regenerated on every process restart. Any page still open in a browser from
// before that restart then carries a hidden __RequestVerificationToken the new key ring can't validate, and
// the very next [ValidateAntiForgeryToken] action (e.g. AccountController.Logout) fails with a 400 - which is
// exactly what made logout flaky across the redeploys/restarts done while building this feature. Pointing
// this at an explicit, stable directory (see ResolveDataProtectionKeysPath below) makes the key ring - and
// therefore every previously-issued token/cookie - survive both restarts and redeploys.
builder.Services.AddDataProtection()
    .SetApplicationName("PresentationManager")
    .PersistKeysToFileSystem(new DirectoryInfo(ResolveDataProtectionKeysPath(builder.Configuration)));

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

// Structured request log (method, path, status code, elapsed ms) for every request - the per-controller
// logging added elsewhere covers *why* an action did what it did, this covers *that it happened at all*,
// including for endpoints (health check, static files) with no logging of their own.
app.UseSerilogRequestLogging();

// Last-resort net: logs any exception an action/middleware itself didn't already log, then lets ASP.NET
// Core's own developer/production error page (or an [ApiController]'s default ProblemDetails response for
// an unhandled exception) run exactly as before - this never changes the response, only guarantees it's on
// record. Placed first so it wraps every middleware/endpoint below it.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Ushlanmagan xatolik: {Method} {Path}", context.Request.Method, context.Request.Path);
        throw;
    }
});

app.UseStaticFiles();

// The MVC web surface's only realistic source of a bare 400 is [ValidateAntiForgeryToken] rejecting a stale
// token (a page left open across a Data Protection key rotation/app restart being the main way that happens -
// see the AddDataProtection call above) - that result carries no body, so the browser renders its own dead-end
// "HTTP ERROR 400" interstitial instead of anything this app owns. Bouncing back to Home/Index (always a
// valid, unauthenticated GET - see HomeController) turns that from a dead end into "you're just signed out,
// here's Kirish again". Scoped to non-/api paths only: the JSON [ApiController]s the WinForms desktop client
// talks to need their real ProblemDetails/400 body untouched.
app.Use(async (context, next) =>
{
    await next();

    if (context.Response.StatusCode == StatusCodes.Status400BadRequest
        && !context.Response.HasStarted
        && !context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Redirect("/");
    }
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Conventional routing for the Judge web platform's MVC controllers (attribute-routed [ApiController]s
// above are unaffected - they never match this pattern since their routes start with "api/"). Home/Index is
// the site's actual root - see HomeController's own doc comment for why (a stable landing page instead of
// dropping straight onto the login form).
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHub<PresentationOrderHub>("/hubs/presentation-order");

// Unauthenticated on purpose - PresentationManager.UI pings this before showing LoginForm to fail fast
// with a friendly message when the server is unreachable, the same way a bad DB connection string used to
// fail fast before this app's own database ever moved behind an API.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
}
catch (Exception ex)
{
    // Mirrors PresentationManager.BotService/Program.cs and PresentationManager.UI/Program.cs's own
    // fatal-startup handling - a bad connection string, missing Jwt:Secret, etc. now lands in the log file
    // (see appsettings.json's "Serilog" section) instead of only ever being visible via `systemctl status`/
    // journald in the moment it happened.
    Log.Fatal(ex, "PresentationManager.API ishga tushirishda halokatli xatolik yuz berdi.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Resolves where uploaded presentation files are stored - identical convention to
/// PresentationManager.BotService/Program.cs's ResolveStorageRoot (a configured "Storage:RootPath", or a
/// local "Files" folder next to this process when unset). Now that PresentationManager.UI talks to files
/// only through this API's own FilesController (see Phase 3), this can simply be a local folder on the
/// server - no shared network path needed.</summary>
static string ResolveStorageRoot(IConfiguration config) =>
    config["Storage:RootPath"] is { Length: > 0 } configured
        ? configured
        : Path.Combine(AppContext.BaseDirectory, "Files");

/// <summary>Resolves where the Data Protection key ring is persisted - a configured "DataProtection:KeysPath",
/// or a "dp-keys" folder next to AppContext.BaseDirectory's own "Files" storage (ResolveStorageRoot above)
/// when unset - same convention, same directory, so it's exactly as redeploy-safe as uploaded files already
/// are in this deployment's actual copy step (a plain overwrite-in-place, not a wipe-then-recopy). Without
/// this configured explicitly, ASP.NET Core has to guess where it's safe to persist the key ring at all, and
/// under a dedicated systemd service user that guess can silently fall back to an in-memory-only key that's
/// regenerated on every restart - see Program.cs's AddDataProtection call for what that breaks.</summary>
static string ResolveDataProtectionKeysPath(IConfiguration config) =>
    config["DataProtection:KeysPath"] is { Length: > 0 } configured
        ? configured
        : Path.Combine(AppContext.BaseDirectory, "dp-keys");
