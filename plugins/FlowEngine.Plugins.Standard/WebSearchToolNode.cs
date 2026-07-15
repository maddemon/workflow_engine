using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Http;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 搜索引擎类型。
/// </summary>
public enum SearchEngineType
{
    /// <summary>Google Search</summary>
    Google,

    /// <summary>Bing Search</summary>
    Bing,

    /// <summary>DuckDuckGo</summary>
    DuckDuckGo,

    /// <summary>SerpAPI</summary>
    SerpAPI,

    /// <summary>自定义搜索引擎</summary>
    Custom
}

/// <summary>
/// Web 搜索工具节点，作为 Agent 的工具被调用。
/// 支持多种搜索引擎配置。
/// </summary>
public sealed class WebSearchToolNode : INodeType
{
    private static readonly HttpExecutionService HttpService = new HttpExecutionService();

    /// <inheritdoc />
    public string TypeName => "webSearchTool";

    /// <inheritdoc />
    public string DisplayName => "Web Search Tool";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "search";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 搜索引擎类型。
    /// </summary>
    [DisplayName("Search Engine")]
    [Description("Search engine to use.")]
    public SearchEngineType SearchEngine { get; set; } = SearchEngineType.Google;

    /// <summary>
    /// API Key 凭据 ID。
    /// </summary>
    [DisplayName("API Key")]
    [Credential(FlowConstants.CredentialFields.ApiKey)]
    [Description("Credential ID for search engine API key.")]
    public string? ApiKeyCredentialId { get; set; }

    /// <summary>
    /// Google Programmable Search Engine ID (cx)。
    /// </summary>
    [DisplayName("Search Engine ID")]
    [Description("Google Programmable Search Engine ID (cx).")]
    public string SearchEngineId { get; set; } = string.Empty;

    /// <summary>
    /// 搜索语言。
    /// </summary>
    [DisplayName("Language")]
    [Description("Search language (e.g. 'en', 'zh-CN').")]
    public string Language { get; set; } = "en";

    /// <summary>
    /// 最大结果数。
    /// </summary>
    [DisplayName("Max Results")]
    [Description("Maximum number of results to return.")]
    public int MaxResults { get; set; } = 5;

    /// <summary>
    /// 自定义搜索端点 URL（Custom 模式）。
    /// </summary>
    [Description("Custom search API endpoint URL (for Custom search engine).")]
    [DisplayCondition(nameof(SearchEngine), SearchEngineType.Custom)]
    public string? CustomEndpoint { get; set; }

    /// <summary>
    /// 自定义请求头（Custom 模式）。
    /// </summary>
    [Description("Custom headers for the search API request.")]
    [Hint(PresentationHint.KeyValueEditor)]
    [DisplayCondition(nameof(SearchEngine), SearchEngineType.Custom)]
    public Dictionary<string, string>? CustomHeaders { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool Output", Direction = PortDirection.Output, Type = PortType.AgentTool }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        return await context.CatchToResult(async ct =>
        {
            // Get search query from LLM input
            var query = GetSearchQuery(context);
            if (string.IsNullOrWhiteSpace(query))
            {
                return context.ErrorResult("MissingQuery", "Search query is required.");
            }

            // Get API key
            var apiKey = await GetApiKeyAsync(context, ct).ConfigureAwait(false);

            // Custom 引擎的 SSRF 预检：用户提供的端点可能指向内网
            if (SearchEngine == SearchEngineType.Custom && !string.IsNullOrWhiteSpace(CustomEndpoint))
            {
                var customUrl = BuildCustomSearchUrl(query);
                var ssrfGuard = context.GuardSsrf(customUrl);
                if (ssrfGuard is not null) return ssrfGuard;
            }

            // 委托给 HttpExecutionService 执行 HTTP 请求（统一处理客户端池、SSRF、异常映射）
            var result = SearchEngine switch
            {
                SearchEngineType.Google => await SearchGoogleAsync(query, apiKey, context, ct).ConfigureAwait(false),
                SearchEngineType.Bing => await SearchBingAsync(query, apiKey, context, ct).ConfigureAwait(false),
                SearchEngineType.DuckDuckGo => await SearchDuckDuckGoAsync(query, context, ct).ConfigureAwait(false),
                SearchEngineType.SerpAPI => await SearchSerpApiAsync(query, apiKey, context, ct).ConfigureAwait(false),
                SearchEngineType.Custom => await SearchCustomAsync(query, apiKey, context, ct).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported search engine: {SearchEngine}")
            };

            if (!result.Success) return result;
            var data = result.Output?.Items?.FirstOrDefault()?.Data;
            return context.Ok(data);
        }, cancellationToken).ConfigureAwait(false);
    }

    private string? GetSearchQuery(NodeExecutionContext context)
    {
        var batch = context.GetInputBatch();
        if (batch.Items.Count > 0)
        {
            var data = batch.Items[0].Data;
            if (data is JsonObject obj)
            {
                // Try common query field names
                if (obj.TryGetPropertyValue("query", out var queryVal))
                {
                    return queryVal?.ToString();
                }
                if (obj.TryGetPropertyValue("q", out var qVal))
                {
                    return qVal?.ToString();
                }
                if (obj.TryGetPropertyValue("search", out var searchVal))
                {
                    return searchVal?.ToString();
                }
            }
            else if (data is JsonValue val)
            {
                return val.ToString();
            }
        }

        // Check ResolvedParameters
        if (context.ResolvedParameters.TryGetValue("query", out var paramQuery))
        {
            return paramQuery?.ToString();
        }

        return null;
    }

    private async Task<string?> GetApiKeyAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        var credential = await context.ResolveCredentialAsync(ApiKeyCredentialId, cancellationToken).ConfigureAwait(false);
        if (credential?.Fields?.TryGetValue(FlowConstants.CredentialFields.ApiKey, out var apiKey) == true)
        {
            return apiKey;
        }

        return null;
    }

    private async Task<NodeExecutionResult> SearchGoogleAsync(string query, string? apiKey, NodeExecutionContext context, CancellationToken cancellationToken)
    {
        // Google Custom Search API
        var url = $"https://www.googleapis.com/customsearch/v1?key={Uri.EscapeDataString(apiKey ?? string.Empty)}&cx={Uri.EscapeDataString(SearchEngineId)}&q={Uri.EscapeDataString(query)}&num={MaxResults}&hl={Language}";

        var request = new HttpExecutionRequest
        {
            Url = url,
            Method = HttpMethod.Get
        };

        return await HttpService.ExecuteAsync(request, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeExecutionResult> SearchBingAsync(string query, string? apiKey, NodeExecutionContext context, CancellationToken cancellationToken)
    {
        // Bing Web Search API
        var url = $"https://api.bing.microsoft.com/v7.0/search?q={Uri.EscapeDataString(query)}&count={MaxResults}&setLang={Language}";

        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(apiKey))
        {
            headers["Ocp-Apim-Subscription-Key"] = apiKey;
        }

        var request = new HttpExecutionRequest
        {
            Url = url,
            Method = HttpMethod.Get,
            Headers = headers
        };

        return await HttpService.ExecuteAsync(request, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeExecutionResult> SearchDuckDuckGoAsync(string query, NodeExecutionContext context, CancellationToken cancellationToken)
    {
        // DuckDuckGo Instant Answer API (no API key required)
        var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";

        var request = new HttpExecutionRequest
        {
            Url = url,
            Method = HttpMethod.Get
        };

        return await HttpService.ExecuteAsync(request, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeExecutionResult> SearchSerpApiAsync(string query, string? apiKey, NodeExecutionContext context, CancellationToken cancellationToken)
    {
        // SerpAPI (Google Search)
        var url = $"https://serpapi.com/search.json?q={Uri.EscapeDataString(query)}&api_key={Uri.EscapeDataString(apiKey ?? string.Empty)}&hl={Language}&num={MaxResults}";

        var request = new HttpExecutionRequest
        {
            Url = url,
            Method = HttpMethod.Get
        };

        return await HttpService.ExecuteAsync(request, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeExecutionResult> SearchCustomAsync(string query, string? apiKey, NodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(CustomEndpoint))
        {
            throw new InvalidOperationException("CustomEndpoint is required for Custom search engine.");
        }

        var url = BuildCustomSearchUrl(query);

        var headers = new Dictionary<string, string>();
        if (CustomHeaders is { Count: > 0 })
        {
            foreach (var (key, value) in CustomHeaders)
            {
                headers[key] = value.Replace("{apiKey}", apiKey ?? string.Empty);
            }
        }

        var request = new HttpExecutionRequest
        {
            Url = url,
            Method = HttpMethod.Get,
            Headers = headers
        };

        return await HttpService.ExecuteAsync(request, context, cancellationToken).ConfigureAwait(false);
    }

    private string BuildCustomSearchUrl(string query)
    {
        return CustomEndpoint!
            .Replace("{query}", Uri.EscapeDataString(query))
            .Replace("{language}", Language)
            .Replace("{maxResults}", MaxResults.ToString());
    }
}
