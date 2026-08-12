using Microsoft.AspNetCore.Mvc;
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

    public AuthController(UserService userService, JwtTokenService jwtTokenService)
    {
        _userService = userService;
        _jwtTokenService = jwtTokenService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await _userService.ValidateLoginAsync(request.Username, request.Password, ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var token = _jwtTokenService.GenerateToken(user);
        return Ok(new LoginResponse(token, UserDto.FromEntity(user)));
    }
}
