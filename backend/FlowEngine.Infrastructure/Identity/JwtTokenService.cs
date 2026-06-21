using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FlowEngine.Application.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FlowEngine.Infrastructure.Identity;

/// <summary>
/// JWT 令牌服务实现。
/// </summary>
public class JwtTokenService(IConfiguration configuration) : ITokenService
{
    /// <inheritdoc />
    public string GenerateAccessToken(Guid userId, string email, IReadOnlyList<string> roles)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT Secret is not configured.");
        var issuer = configuration["Jwt:Issuer"] ?? "FlowEngine";
        var audience = configuration["Jwt:Audience"] ?? "FlowEngine";
        var expirationMinutes = int.TryParse(configuration["Jwt:ExpirationMinutes"], out var parsed)
            ? parsed
            : 60;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
