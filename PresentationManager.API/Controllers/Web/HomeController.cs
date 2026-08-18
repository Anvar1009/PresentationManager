using PresentationManager.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace PresentationManager.API.Controllers.Web;

/// <summary>The site's actual root ("/") - Program.cs's default route and AccountController.Logout both land
/// here rather than straight on the login form, so there's always one stable "home" page to come back to
/// (unauthenticated visit, or right after signing out) with just a "Kirish" link on it. An already-signed-in
/// visitor lands here too (e.g. a bookmarked "/") and is bounced straight to their own role's dashboard,
/// same switch AccountController.Login uses after a fresh sign-in.</summary>
public sealed class HomeController : Controller
{
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return View();
        }

        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        return role switch
        {
            nameof(UserRole.Judge) => RedirectToAction("Dashboard", "Judge"),
            nameof(UserRole.OrderOperator) => RedirectToAction("Dashboard", "Order"),
            nameof(UserRole.Admin) => RedirectToAction("Dashboard", "Admin"),
            nameof(UserRole.SuperAdmin) => RedirectToAction("Dashboard", "SuperAdmin"),
            _ => View()
        };
    }
}
