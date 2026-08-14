using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Models;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Controllers.Web;

/// <summary>Cookie-based login shared by both web-only roles (<see cref="UserRole.Judge"/> and
/// <see cref="UserRole.OrderOperator"/>) - separate from <see cref="Controllers.AuthController"/>'s JWT login
/// (used by the WinForms desktop app), but backed by the exact same <see cref="UserService.ValidateLoginAsync"/>
/// and the same Users table. Every other role gets the same generic "login yoki parol noto'g'ri" as a wrong
/// password, so this page never reveals which usernames exist or what role they hold.</summary>
public sealed class AccountController : Controller
{
    private static readonly HashSet<UserRole> WebRoles = [UserRole.Judge, UserRole.OrderOperator];

    private readonly UserService _userService;

    public AccountController(UserService userService)
    {
        _userService = userService;
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
            ModelState.AddModelError(string.Empty, "Login yoki parol noto'g'ri.");
            return View(model);
        }

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
            UserRole.OrderOperator => RedirectToAction("Dashboard", "Order"),
            _ => RedirectToAction("Dashboard", "Judge")
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
