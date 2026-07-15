using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Http;

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
    /// 重试基础延迟（毫秒）。默认 1000ms，实际延迟 = RetryBaseDelayMs * (2 ^ attempt)。
    /// </summary>
    internal int RetryBaseDelayMs { get; set; } = 1000;

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
                var delayMs = RetryBaseDelayMs * (1 << attempt);
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
        var httpMethod = string.IsNullOrWhiteSpace(request.HttpMethod)
            ? HttpMethod.Post
            : new HttpMethod(request.HttpMethod);

        // 1. 构建逻辑参数（key 为逻辑名，便于 ParamNameMap 重命名）
        var logicalParams = BuildLogicalParams(request);

        // 2. 应用参数名映射（如 clientId→appkey）
        var requestParams = ApplyNameMap(logicalParams, request.ParamNameMap);

        // 3. 按参数位置拼装请求（Query 拼到 URL；否则放入 form 请求体）
        var url = request.TokenUrl;
        if (request.ParamLocation == OAuth2ParamLocation.Query)
        {
            url = AppendQuery(url, requestParams);
        }

        if (SsrfGuard.IsInternalTarget(request.TokenUrl))
        {
            throw new BusinessException("OAuth2 token URL 指向内部地址，已被 SSRF 防护拦截");
        }

        using var httpRequest = new HttpRequestMessage(httpMethod, url);
        if (request.ParamLocation != OAuth2ParamLocation.Query && requestParams.Count > 0)
        {
            httpRequest.Content = new FormUrlEncodedContent(requestParams!);
        }

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode >= HttpStatusCode.InternalServerError)
        {
            // 5xx 由外层重试处理
            throw new HttpRequestException(
                $"OAuth2 token 端点返回 HTTP {(int)response.StatusCode}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(
                $"OAuth2 token 端点返回 HTTP {(int)response.StatusCode}");
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

        // 4. 业务错误码判定（如钉钉 errcode != 0 但 HTTP 200）
        if (!string.IsNullOrWhiteSpace(request.ResponseErrorPath))
        {
            var errorNode = NavigatePath(raw, request.ResponseErrorPath);
            if (errorNode is not null)
            {
                var errorValue = errorNode.ToString();
                var successValues = request.ResponseSuccessValues;
                var isSuccess = successValues is null || successValues.Count == 0
                    || successValues.Exists(v => string.Equals(v, errorValue, StringComparison.Ordinal));
                if (!isSuccess)
                {
                    throw new BusinessException(
                        $"OAuth2 令牌端点业务错误（{request.ResponseErrorPath}={errorValue}）");
                }
            }
        }

        var tokenPath = string.IsNullOrWhiteSpace(request.TokenPath) ? "access_token" : request.TokenPath;
        var accessTokenNode = NavigatePath(raw, tokenPath);
        if (accessTokenNode is null)
        {
            throw new BusinessException($"OAuth2 响应中未找到令牌路径 '{tokenPath}'。");
        }

        // 健壮提取：访问令牌路径可能指向非 JsonValue（如嵌套对象/数组）。
        // GetValue<string>() 在非 JsonValue 上会抛 InvalidOperationException，需避免被外层重试逻辑误吞。
        var accessToken = ExtractStringToken(accessTokenNode);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new BusinessException($"OAuth2 令牌未找到或为空，路径 '{tokenPath}'。");
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

    private static Dictionary<string, string?> BuildLogicalParams(OAuth2TokenRequest request)
    {
        var parameters = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.GrantType))
        {
            parameters["grant_type"] = request.GrantType;
        }

        parameters["clientId"] = request.ClientId;
        parameters["clientSecret"] = request.ClientSecret;

        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            parameters["scope"] = request.Scope;
        }

        if (request.ExtraParameters is not null)
        {
            foreach (var (key, value) in request.ExtraParameters)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    parameters[key] = value;
                }
            }
        }

        return parameters;
    }

    private static Dictionary<string, string?> ApplyNameMap(
        Dictionary<string, string?> logical, Dictionary<string, string>? nameMap)
    {
        if (nameMap is null || nameMap.Count == 0)
        {
            return logical;
        }

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in logical)
        {
            var actual = nameMap.TryGetValue(key, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
                ? mapped
                : key;
            result[actual] = value;
        }

        return result;
    }

    private static string AppendQuery(string url, Dictionary<string, string?> parameters)
    {
        var pairs = parameters
            .Where(kv => kv.Value is not null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}")
            .ToList();
        if (pairs.Count == 0)
        {
            return url;
        }

        return url + (url.Contains('?') ? "&" : "?") + string.Join("&", pairs);
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

    /// <summary>
    /// 从 JSON 节点安全提取字符串令牌。
    /// 若节点为 <see cref="JsonValue"/>，直接取其字符串值；
    /// 否则（如嵌套对象/数组）退化为 <see cref="JsonNode.ToString"/> 并去掉两侧引号，避免 <c>GetValue&lt;string&gt;()</c> 抛出的 <see cref="InvalidOperationException"/>。
    /// </summary>
    private static string? ExtractStringToken(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            return value.GetValue<string?>();
        }

        // 非 JsonValue（对象/数组）：退化为原始 JSON 文本，并去除可能的外层引号。
        var raw = node.ToString();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            raw = raw[1..^1];
        }

        return raw;
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

    /// <summary>
    /// 生成确定性缓存键。
    /// </summary>
    public static string ComputeCacheKey(string credentialName, string tokenUrl, string clientId, string? scope, string? grantType)
    {
        var raw = $"{credentialName}|{tokenUrl}|{clientId}|{scope ?? string.Empty}|{grantType ?? "client_credentials"}";
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
