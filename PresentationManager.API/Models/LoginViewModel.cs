using System.ComponentModel.DataAnnotations;

namespace PresentationManager.API.Models;

/// <summary>Judge web login form model - see Controllers\Web\AccountController.</summary>
public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Login kiritilishi shart.")]
    [Display(Name = "Login")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parol kiritilishi shart.")]
    [Display(Name = "Parol")]
    public string Password { get; set; } = string.Empty;
}
