using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FlowEngine.Application.Identity;
using FlowEngine.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Identity;

public sealed class JwtTokenServiceTests
{
    private static IConfiguration CreateConfiguration(Dictionary<string, string?>? values = null)
    {
        var defaults = new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "super-secret-key-for-unit-tests-only!",
            ["Jwt:Issuer"] = "FlowEngine.Tests",
            ["Jwt:Audience"] = "FlowEngine.Tests.Audience",
            ["Jwt:ExpirationMinutes"] = "30",
        };

        if (values is not null)
        {
            foreach (var kv in values)
            {
                defaults[kv.Key] = kv.Value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(defaults)
            .Build();
    }

    [Fact]
    public void GenerateAccessToken_ValidConfig_ReturnsNonEmptyToken()
    {
        var service = new JwtTokenService(CreateConfiguration());

        var token = service.GenerateAccessToken(Guid.NewGuid(), "user@example.com", ["User"]);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void GenerateAccessToken_TokenContainsUserClaims()
    {
        var userId = Guid.NewGuid();
        const string email = "user@example.com";
        var roles = new[] { "Admin", "User" };
        var service = new JwtTokenService(CreateConfiguration());

        var token = service.GenerateAccessToken(userId, email, roles);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(userId.ToString(), jwt.Subject);
        Assert.Equal(email, jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(roles, jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray());
    }

    [Fact]
    public void GenerateAccessToken_MissingSecret_ThrowsInvalidOperationException()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new JwtTokenService(configuration);

        Assert.Throws<InvalidOperationException>(() =>
            service.GenerateAccessToken(Guid.NewGuid(), "user@example.com", []));
    }

    [Fact]
    public void GenerateAccessToken_DefaultIssuerAndAudience_WhenNotConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "super-secret-key-for-unit-tests-only!",
            })
            .Build();
        var service = new JwtTokenService(configuration);

        var token = service.GenerateAccessToken(Guid.NewGuid(), "user@example.com", ["User"]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("FlowEngine", jwt.Issuer);
        Assert.Equal("FlowEngine", jwt.Audiences.Single());
    }

    [Fact]
    public void GenerateAccessToken_DefaultExpiration_WhenNotConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "super-secret-key-for-unit-tests-only!",
            })
            .Build();
        var service = new JwtTokenService(configuration);
        var before = DateTime.UtcNow;

        var token = service.GenerateAccessToken(Guid.NewGuid(), "user@example.com", []);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.True(jwt.ValidTo >= before.AddMinutes(55));
        Assert.True(jwt.ValidTo <= before.AddMinutes(65));
    }

    [Fact]
    public void GenerateAccessToken_TokenCanBeValidatedWithSameKey()
    {
        const string secret = "super-secret-key-for-unit-tests-only!";
        var configuration = CreateConfiguration(new Dictionary<string, string?> { ["Jwt:Secret"] = secret });
        var service = new JwtTokenService(configuration);
        var token = service.GenerateAccessToken(Guid.NewGuid(), "user@example.com", ["User"]);

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = "FlowEngine.Tests",
            ValidateAudience = true,
            ValidAudience = "FlowEngine.Tests.Audience",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        tokenHandler.ValidateToken(token, validationParameters, out _);
    }
}
