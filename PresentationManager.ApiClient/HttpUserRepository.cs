using System.Net;
using System.Net.Http.Json;
using PresentationManager.ApiClient.Wire;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.ApiClient;

public sealed class HttpUserRepository : IUserRepository
{
    private readonly HttpClient _http;

    public HttpUserRepository(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<User>> GetAllAsync(CancellationToken ct = default)
    {
        var wires = await _http.GetFromJsonAsync<List<UserWire>>("api/users", ApiJsonOptions.Default, ct) ?? [];
        return wires.Select(w => w.ToEntity()).ToList();
    }

    public Task<User?> GetByIdAsync(int id, CancellationToken ct = default) =>
        GetOneAsync($"api/users/{id}", ct);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        GetOneAsync($"api/users/by-username/{Uri.EscapeDataString(username)}", ct);

    public Task<User?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default) =>
        GetOneAsync($"api/users/by-telegram/{telegramChatId}", ct);

    public Task<User?> GetByTelegramUsernameAsync(string telegramUsername, CancellationToken ct = default) =>
        GetOneAsync($"api/users/by-telegram-username/{Uri.EscapeDataString(telegramUsername)}", ct);

    public async Task<User> AddAsync(User user, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/users", CreateUserWireRequest.FromEntity(user), ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<UserWire>(ApiJsonOptions.Default, ct)
            ?? throw new InvalidOperationException("Create user response body was empty.");
        // The API never echoes the hash back (see UserDto) - the caller (UserService) never reads it off
        // the returned User for a freshly-created account anyway, only off ones it already computed itself.
        var created = wire.ToEntity();
        created.PasswordHash = user.PasswordHash;
        return created;
    }

    public async Task<int> CountAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<int>("api/users/count", ApiJsonOptions.Default, ct);

    public async Task SetTelegramLinkAsync(int userId, long telegramChatId, string? telegramUsername, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"api/users/{userId}/telegram-link", new { TelegramChatId = telegramChatId, TelegramUsername = telegramUsername }, ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetTelegramLinkTokenAsync(int userId, string? token, DateTime? expiresAtUtc, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"api/users/{userId}/telegram-link-token", new { Token = token, ExpiresAtUtc = expiresAtUtc }, ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<User?> GetByTelegramLinkTokenAsync(string token, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/users/by-telegram-link-token/{Uri.EscapeDataString(token)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var lookup = await response.Content.ReadFromJsonAsync<TelegramLinkTokenLookupWire>(ApiJsonOptions.Default, ct);
        if (lookup is null)
        {
            return null;
        }

        // Only what AdminLinkService.TryConsumeAsync actually reads off the result (Id, expiry) - see
        // PresentationManager.API.Controllers.UsersController.GetByTelegramLinkToken for why the token
        // string itself and the rest of the account aren't exposed here.
        return new User
        {
            Id = lookup.UserId,
            Username = string.Empty,
            PasswordHash = string.Empty,
            FullName = string.Empty,
            TelegramLinkTokenExpiresAtUtc = lookup.ExpiresAtUtc
        };
    }

    public async Task SetPasswordAsync(int userId, string passwordHash, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"api/users/{userId}/password", new { PasswordHash = passwordHash }, ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetUsernameAsync(int userId, string username, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"api/users/{userId}/username", new { Username = username }, ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetFullNameAsync(int userId, string fullName, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"api/users/{userId}/fullname", new { FullName = fullName }, ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetRoleAsync(int userId, UserRole role, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"api/users/{userId}/role", new { Role = role }, ApiJsonOptions.Default, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<User?> GetOneAsync(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<UserWire>(ApiJsonOptions.Default, ct);
        return wire?.ToEntity();
    }

    private sealed record TelegramLinkTokenLookupWire(int UserId, DateTime? ExpiresAtUtc);
}
