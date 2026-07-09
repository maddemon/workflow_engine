using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Runtime.Credentials;

/// <summary>
/// OAuth2 令牌服务默认实现。
/// </summary>
public sealed class OAuth2TokenService : IOAuth2TokenService, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache;

    /// <summary>
    /// 过期前缓冲秒数，默认 60 秒。
    /// </summary>
    public int RefreshBufferSeconds { get; set; } = 60;

    /// <summary>
    /// 最大重试次数。默认 3，即最多 4 次总请求（首次 + 3 次重试）。
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 初始化令牌服务。
    /// </summary>
    public OAuth2TokenService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _cache = new ConcurrentDictionary<string, CacheEntry>();
    }

    /// <inheritdoc />
    public async Task<OAuth2TokenResponse> GetTokenAsync(OAuth2TokenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TokenUrl))
        {
            throw new BusinessException("OAuth2 tokenUrl 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            throw new BusinessException("OAuth2 clientId 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            throw new BusinessException("OAuth2 clientSecret 不能为空。");
        }

        var attempt = 0;
        Exception? lastException = null;
        while (attempt <= MaxRetries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await RequestTokenAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (BusinessException)
            {
                // 业务异常（如非 2xx 且不适用重试）直接抛出
                throw;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
            }
            catch (TaskCanceledException ex)
            {
                // 用户主动取消时直接抛出；否则将超时类取消视为可重试
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                lastException = ex;
            }

            if (attempt < MaxRetries)
            {
                var delayMs = 1000 * (1 << attempt);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }

            attempt++;
        }

        throw new BusinessException(
            $"OAuth2 令牌端点请求失败，已重试 {MaxRetries} 次。",
            lastException ?? new InvalidOperationException("未知错误"));
    }

    /// <inheritdoc />
    public async Task<OAuth2TokenResponse> GetOrRefreshTokenAsync(string cacheKey, OAuth2TokenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(request);

        if (_cache.TryGetValue(cacheKey, out var entry) && !IsExpired(entry))
        {
            return entry.Response;
        }

        var response = await GetTokenAsync(request, cancellationToken).ConfigureAwait(false);
        _cache[cacheKey] = new CacheEntry
        {
            Response = response,
            ExpiresAt = ComputeExpiresAt(response)
        };
        return response;
    }

    private async Task<OAuth2TokenResponse> RequestTokenAsync(OAuth2TokenRequest request, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(BuildFormBody(request));
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.TokenUrl) { Content = content };

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode >= HttpStatusCode.InternalServerError)
        {
            // 5xx 由外层重试处理
            throw new HttpRequestException(
                $"OAuth2 token 端点返回 {(int)response.StatusCode}：{Truncate(responseBody)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(
                $"OAuth2 token 端点返回 {(int)response.StatusCode}：{Truncate(responseBody)}");
        }

        JsonNode? raw;
        try
        {
            raw = JsonNode.Parse(responseBody);
        }
        catch
        {
            raw = null;
        }

        if (raw is null)
        {
            throw new BusinessException("OAuth2 token 端点返回非 JSON 响应。");
        }

        var tokenPath = string.IsNullOrWhiteSpace(request.TokenPath) ? "access_token" : request.TokenPath;
        var accessTokenNode = NavigatePath(raw, tokenPath);
        if (accessTokenNode is null)
        {
            throw new BusinessException($"OAuth2 响应中未找到令牌路径 '{tokenPath}'。");
        }

        var accessToken = accessTokenNode.GetValue<string>();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new BusinessException("OAuth2 响应中访问令牌为空。");
        }

        var tokenType = GetString(raw, "token_type") ?? "Bearer";
        var expiresIn = GetLong(raw, "expires_in");
        var refreshToken = GetString(raw, "refresh_token");
        var scope = GetString(raw, "scope");

        return new OAuth2TokenResponse
        {
            AccessToken = accessToken,
            TokenType = tokenType,
            ExpiresIn = expiresIn,
            RefreshToken = refreshToken,
            Scope = scope,
            Raw = raw
        };
    }

    private static Dictionary<string, string?> BuildFormBody(OAuth2TokenRequest request)
    {
        var body = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["grant_type"] = request.GrantType,
            ["client_id"] = request.ClientId,
            ["client_secret"] = request.ClientSecret
        };

        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            body["scope"] = request.Scope;
        }

        if (request.ExtraParameters is not null)
        {
            foreach (var (key, value) in request.ExtraParameters)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    body[key] = value;
                }
            }
        }

        return body;
    }

    private bool IsExpired(CacheEntry entry)
    {
        return DateTimeOffset.UtcNow.AddSeconds(RefreshBufferSeconds) >= entry.ExpiresAt;
    }

    private static DateTimeOffset ComputeExpiresAt(OAuth2TokenResponse response)
    {
        if (response.ExpiresIn.HasValue && response.ExpiresIn.Value > 0)
        {
            return DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn.Value);
        }

        return DateTimeOffset.MaxValue;
    }

    private static JsonNode? NavigatePath(JsonNode? node, string path)
    {
        if (node is null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var current = node;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var child))
            {
                current = child;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private static string? GetString(JsonNode? node, string key)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) && value is not null)
        {
            return value.GetValue<string?>();
        }

        return null;
    }

    private static long? GetLong(JsonNode? node, string key)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) && value is not null)
        {
            return value.GetValue<long?>();
        }

        return null;
    }

    private static string Truncate(string value, int maxLength = 500)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }

    /// <summary>
    /// 生成确定性缓存键。
    /// </summary>
    public static string ComputeCacheKey(string credentialName, string tokenUrl, string? scope, string? grantType)
    {
        var raw = $"{credentialName}|{tokenUrl}|{scope ?? string.Empty}|{grantType ?? "client_credentials"}";
        var bytes = Encoding.UTF8.GetBytes(raw);
#if NET8_0_OR_GREATER
        var hash = SHA256.HashData(bytes);
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
#endif
        return Convert.ToHexString(hash)[..16];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cache.Clear();
    }

    private sealed class CacheEntry
    {
        public OAuth2TokenResponse Response { get; set; } = null!;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
