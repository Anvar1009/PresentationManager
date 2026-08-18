using Microsoft.AspNetCore.Mvc;

namespace PresentationManager.API.Controllers.Web;

/// <summary>The site's actual root ("/") - Program.cs's default route and AccountController.Logout both land
/// here rather than straight on the login form, so there's always one stable "home" page to come back to
/// (unauthenticated visit, or right after signing out) with just a "Kirish" link on it. Always renders the
/// same landing page regardless of auth state - no role-based redirect branch here, so a visit right after
/// signing out can never end up bounced back into a dashboard by anything sitting between them.</summary>
public sealed class HomeController : Controller
{
    public IActionResult Index() => View();
}
