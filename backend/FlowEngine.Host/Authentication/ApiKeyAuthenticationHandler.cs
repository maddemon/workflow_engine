using System.Security.Claims;
using System.Text.Encodings.Web;
using FlowEngine.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FlowEngine.Host.Authentication;

/// <summary>
/// API Key 认证方案选项。
/// </summary>
public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// API Key 认证处理器，处理 <c>Authorization: Bearer &lt;apiKey&gt;</c> 中非 JWT 的 API Key。
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ApiKeyService apiKeyService)
    : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const string BearerPrefix = "Bearer ";

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var key = authHeader[BearerPrefix.Length..];
        if (string.IsNullOrWhiteSpace(key))
        {
            return AuthenticateResult.NoResult();
        }

        // JWT 格式包含三段以 '.' 分隔；API Key 不应走此处理器。
        if (key.Count(c => c == '.') == 2)
        {
            return AuthenticateResult.NoResult();
        }

        var userId = await apiKeyService.ValidateAsync(key, Context.RequestAborted).ConfigureAwait(false);
        if (userId is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.Value.ToString()),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
