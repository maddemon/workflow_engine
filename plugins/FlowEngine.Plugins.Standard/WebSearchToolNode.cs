using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Http;
using FlowEngine.Core.Scripting;

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
[NodeMeta(TypeName = "webSearchTool", DisplayName = "Web Search Tool", Category = NodeCategory.AI, Icon = "search", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
[Port(FlowConstants.PortNames.Tools, "Tool Output", PortDirection.Output, PortType.AgentTool)]
public sealed class WebSearchToolNode : NodeBase
{
    private static readonly HttpExecutionService HttpService = new HttpExecutionService();

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
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            // Get search query from LLM input
            var query = GetSearchQuery(input);
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new NodeExecutionException("MissingQuery", "Search query is required.");
            }

            // Get API key
            var apiKey = await GetApiKeyAsync(ct).ConfigureAwait(false);

            // Custom 引擎的 SSRF 预检：用户提供的端点可能指向内网
            if (SearchEngine == SearchEngineType.Custom && !string.IsNullOrWhiteSpace(CustomEndpoint))
            {
                var customUrl = BuildCustomSearchUrl(query);
                var ssrfBlock = GuardSsrf(customUrl);
                if (ssrfBlock is not null)
                {
                    throw new NodeExecutionException(ssrfBlock.Error!.Code, ssrfBlock.Error.Message);
                }
            }

            // 委托给 HttpExecutionService 执行 HTTP 请求（统一处理客户端池、SSRF、异常映射）
            var result = SearchEngine switch
            {
                SearchEngineType.Google => await SearchGoogleAsync(query, apiKey, ct).ConfigureAwait(false),
                SearchEngineType.Bing => await SearchBingAsync(query, apiKey, ct).ConfigureAwait(false),
                SearchEngineType.DuckDuckGo => await SearchDuckDuckGoAsync(query, ct).ConfigureAwait(false),
                SearchEngineType.SerpAPI => await SearchSerpApiAsync(query, apiKey, ct).ConfigureAwait(false),
                SearchEngineType.Custom => await SearchCustomAsync(query, apiKey, ct).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported search engine: {SearchEngine}")
            };

            if (!result.Success)
            {
                throw new NodeExecutionException(result.Error!.Code, result.Error.Message);
            }

            var data = result.Output?.Items?.FirstOrDefault()?.Data;
            return Single(data);
        }
        catch (OperationCanceledException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "Web search was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.ScriptError, $"Expression evaluation failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error during web search: {ex.Message}");
        }
    }

    private string? GetSearchQuery(NodeInput input)
    {
        var batch = input.InputBatch;
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
        var paramQuery = GetResolvedParameter("query");
        return paramQuery?.ToString();
    }

    private async Task<string?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        var credential = await GetCredentialAsync(ApiKeyCredentialId, cancellationToken).ConfigureAwait(false);
        if (credential?.Fields?.TryGetValue(FlowConstants.CredentialFields.ApiKey, out var apiKey) == true)
        {
            return apiKey;
        }

        return null;
    }

    private async Task<NodeExecutionResult> SearchGoogleAsync(string query, string? apiKey, CancellationToken cancellationToken)
    {
        // Google Custom Search API
        var url = $"https://www.googleapis.com/customsearch/v1?key={Uri.EscapeDataString(apiKey ?? string.Empty)}&cx={Uri.EscapeDataString(SearchEngineId)}&q={Uri.EscapeDataString(query)}&num={MaxResults}&hl={Language}";

        var request = new HttpExecutionRequest
        {
            Url = url,
            Method = HttpMethod.Get
        };

        return await HttpService.ExecuteAsync(request, ExecutionContext, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeExecutionResult> SearchBingAsync(string query, string? apiKey, CancellationToken cancellationToken)
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

        return await HttpService.ExecuteAsync(request, ExecutionContext, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeExecutionResult> SearchDuckDuckGoAsync(string query, CancellationToken cancellationToken)
    {
        // DuckDuckGo Instant Answer API (no API key required)
        var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";

        var request = new HttpExecutionRequest
        {
            Url = url,
            Method = HttpMethod.Get
        };

        return await HttpService.ExecuteAsync(request, ExecutionContext, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeExecutionResult> SearchSerpApiAsync(string query, string? apiKey, CancellationToken cancellationToken)
    {
        // SerpAPI (Google Search) — apiKey 优先通过 Header 传递，避免落入 URL 日志
        var url = $"https://serpapi.com/search.json?q={Uri.EscapeDataString(query)}&hl={Language}&num={MaxResults}";

        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(apiKey))
        {
            headers["api_key"] = apiKey;
        }

        var request = new HttpExecutionRequest
        {
            Url = url,
            Method = HttpMethod.Get,
            Headers = headers
        };

        return await HttpService.ExecuteAsync(request, ExecutionContext, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeExecutionResult> SearchCustomAsync(string query, string? apiKey, CancellationToken cancellationToken)
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

        return await HttpService.ExecuteAsync(request, ExecutionContext, cancellationToken).ConfigureAwait(false);
    }

    private string BuildCustomSearchUrl(string query)
    {
        return CustomEndpoint!
            .Replace("{query}", Uri.EscapeDataString(query))
            .Replace("{language}", Language)
            .Replace("{maxResults}", MaxResults.ToString());
    }

    /// <summary>
    /// 构造单数据项的成功输出。
    /// </summary>
    private static NodeHandlerOutput Single(JsonNode? data) =>
        NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = data,
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });
}
