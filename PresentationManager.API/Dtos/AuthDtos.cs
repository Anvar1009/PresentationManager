namespace PresentationManager.API.Dtos;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string Token, UserDto User);
