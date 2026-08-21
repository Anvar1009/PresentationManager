using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationManager.API.Models;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Controllers.Web;

/// <summary>Cookie-based login shared by every role with a web surface (<see cref="UserRole.Judge"/>,
/// <see cref="UserRole.OrderOperator"/>, <see cref="UserRole.Admin"/>, <see cref="UserRole.SuperAdmin"/>) -
/// separate from <see cref="Controllers.AuthController"/>'s JWT login (used by the WinForms desktop app), but
/// backed by the exact same <see cref="UserService.ValidateLoginAsync"/> and the same Users table.
/// <see cref="UserRole.Operator"/> gets the same generic "login yoki parol noto'g'ri" as a wrong password
/// (desktop-only, no web screens exist for it), so this page never reveals which usernames exist or what
/// role they hold.</summary>
public sealed class AccountController : Controller
{
    private static readonly HashSet<UserRole> WebRoles =
        [UserRole.Judge, UserRole.OrderOperator, UserRole.Admin, UserRole.SuperAdmin];

    private readonly UserService _userService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(UserService userService, ILogger<AccountController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login() => View(new LoginViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userService.ValidateLoginAsync(model.Username, model.Password, ct);
        if (user is null || !WebRoles.Contains(user.Role))
        {
            _logger.LogWarning("Veb kirish muvaffaqiyatsiz: {Username}", model.Username);
            ModelState.AddModelError(string.Empty, "Login yoki parol noto'g'ri.");
            return View(model);
        }

        _logger.LogInformation("Veb kirish muvaffaqiyatli: {Username} ({Role})", user.Username, user.Role);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        // Only meaningful for Judge (powers JudgeController's assignment lookup via
        // JudgeService.GetLinkedAssignmentsByChatIdAsync - see UserRole.Judge's own doc comment) - harmless
        // to include for OrderOperator too, which just never reads this claim.
        if (user.TelegramChatId is { } chatId)
        {
            claims.Add(new Claim("TelegramChatId", chatId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return user.Role switch
        {
            UserRole.Judge => RedirectToAction("Dashboard", "Judge"),
            UserRole.OrderOperator => RedirectToAction("Dashboard", "Order"),
            UserRole.Admin => RedirectToAction("Dashboard", "Admin"),
            UserRole.SuperAdmin => RedirectToAction("Dashboard", "SuperAdmin"),
            _ => throw new InvalidOperationException($"'{user.Role}' roli uchun veb sahifa mavjud emas.")
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        _logger.LogInformation("Veb chiqish: {Username}", User.Identity?.Name);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }
}
