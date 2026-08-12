using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Services;

/// <summary>Issues the bearer token every authenticated request from PresentationManager.UI carries,
/// signed with the same Jwt:Secret this API validates incoming tokens against (see Program.cs's
/// AddJwtBearer setup). Session-length expiry matches the desktop app's existing behavior - it already
/// requires a fresh login on every launch, so there is no "remember me" to support.</summary>
public sealed class JwtTokenService
{
    private readonly SigningCredentials _signingCredentials;
    private readonly string _issuer;
    private readonly TimeSpan _expiry;

    public JwtTokenService(string secret, string issuer, TimeSpan expiry)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _issuer = issuer;
        _expiry = expiry;
    }

    public string GenerateToken(User user)
    {
        Claim[] claims =
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        ];

        var token = new JwtSecurityToken(
            issuer: _issuer,
            claims: claims,
            expires: DateTime.UtcNow.Add(_expiry),
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
