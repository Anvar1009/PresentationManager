using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationManager.API.Dtos;
using PresentationManager.API.Services;
using PresentationManager.Application.Services;

namespace PresentationManager.API.Controllers;

/// <summary>The one place a password is ever compared against its stored hash - every other process
/// (PresentationManager.UI) only ever sees the token/user this hands back, never the hash itself.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserService _userService;
    private readonly JwtTokenService _jwtTokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(UserService userService, JwtTokenService jwtTokenService, ILogger<AuthController> logger)
    {
        _userService = userService;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await _userService.ValidateLoginAsync(request.Username, request.Password, ct);
        if (user is null)
        {
            _logger.LogWarning("Kirish muvaffaqiyatsiz (JWT): {Username}", request.Username);
            return Unauthorized();
        }

        var token = _jwtTokenService.GenerateToken(user);
        _logger.LogInformation("Kirish muvaffaqiyatli (JWT): {Username} ({Role})", user.Username, user.Role);
        return Ok(new LoginResponse(token, UserDto.FromEntity(user)));
    }
}
