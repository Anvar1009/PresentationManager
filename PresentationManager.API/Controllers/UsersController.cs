using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresentationManager.API.Dtos;
using PresentationManager.Application.Interfaces;

namespace PresentationManager.API.Controllers;

/// <summary>Thin HTTP mirror of <see cref="IUserRepository"/> - every read returns the safe
/// <see cref="UserDto"/> (never a password hash or Telegram link token). Account-management writes
/// (create, role change, password/login reset) are SuperAdmin-only, matching who can reach these actions
/// today (SuperAdminPanelForm) - everything else stays open to any authenticated desktop user, since
/// Admin/Operator accounts also drive the "Botga ulash" Telegram-link flow.</summary>
[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;

    public UsersController(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll(CancellationToken ct)
    {
        var users = await _userRepository.GetAllAsync(ct);
        return Ok(users.Select(UserDto.FromEntity).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> GetById(int id, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(id, ct);
        return user is null ? NotFound() : Ok(UserDto.FromEntity(user));
    }

    [HttpGet("by-username/{username}")]
    public async Task<ActionResult<UserDto>> GetByUsername(string username, CancellationToken ct)
    {
        var user = await _userRepository.GetByUsernameAsync(username, ct);
        return user is null ? NotFound() : Ok(UserDto.FromEntity(user));
    }

    [HttpGet("by-telegram/{chatId:long}")]
    public async Task<ActionResult<UserDto>> GetByTelegramChatId(long chatId, CancellationToken ct)
    {
        var user = await _userRepository.GetByTelegramChatIdAsync(chatId, ct);
        return user is null ? NotFound() : Ok(UserDto.FromEntity(user));
    }

    [HttpGet("by-telegram-username/{username}")]
    public async Task<ActionResult<UserDto>> GetByTelegramUsername(string username, CancellationToken ct)
    {
        var user = await _userRepository.GetByTelegramUsernameAsync(username, ct);
        return user is null ? NotFound() : Ok(UserDto.FromEntity(user));
    }

    [HttpGet("count")]
    public async Task<ActionResult<int>> Count(CancellationToken ct) => Ok(await _userRepository.CountAsync(ct));

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<UserDto>> Add(CreateUserRequest request, CancellationToken ct)
    {
        var created = await _userRepository.AddAsync(request.ToEntity(), ct);
        return Ok(UserDto.FromEntity(created));
    }

    [HttpPut("{id:int}/telegram-link")]
    public async Task<IActionResult> SetTelegramLink(int id, SetTelegramLinkRequest request, CancellationToken ct)
    {
        await _userRepository.SetTelegramLinkAsync(id, request.TelegramChatId, request.TelegramUsername, ct);
        return NoContent();
    }

    [HttpPut("{id:int}/telegram-link-token")]
    public async Task<IActionResult> SetTelegramLinkToken(int id, SetTelegramLinkTokenRequest request, CancellationToken ct)
    {
        await _userRepository.SetTelegramLinkTokenAsync(id, request.Token, request.ExpiresAtUtc, ct);
        return NoContent();
    }

    [HttpGet("by-telegram-link-token/{token}")]
    public async Task<ActionResult<TelegramLinkTokenLookupDto>> GetByTelegramLinkToken(string token, CancellationToken ct)
    {
        var user = await _userRepository.GetByTelegramLinkTokenAsync(token, ct);
        return user is null ? NotFound() : Ok(new TelegramLinkTokenLookupDto(user.Id, user.TelegramLinkTokenExpiresAtUtc));
    }

    [HttpPut("{id:int}/password")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> SetPassword(int id, SetPasswordRequest request, CancellationToken ct)
    {
        await _userRepository.SetPasswordAsync(id, request.PasswordHash, ct);
        return NoContent();
    }

    [HttpPut("{id:int}/username")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> SetUsername(int id, SetUsernameRequest request, CancellationToken ct)
    {
        await _userRepository.SetUsernameAsync(id, request.Username, ct);
        return NoContent();
    }

    [HttpPut("{id:int}/fullname")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> SetFullName(int id, SetFullNameRequest request, CancellationToken ct)
    {
        await _userRepository.SetFullNameAsync(id, request.FullName, ct);
        return NoContent();
    }

    [HttpPut("{id:int}/role")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> SetRole(int id, SetRoleRequest request, CancellationToken ct)
    {
        await _userRepository.SetRoleAsync(id, request.Role, ct);
        return NoContent();
    }
}
