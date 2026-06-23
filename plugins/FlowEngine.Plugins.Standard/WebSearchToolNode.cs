using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Http;

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
/// 参考 n8n 的 ToolSerpApi 设计。
/// </summary>
public sealed class WebSearchToolNode : INodeType
{
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
    [Description("Search engine to use.")]
    public SearchEngineType SearchEngine { get; set; } = SearchEngineType.Google;

    /// <summary>
    /// API Key 凭据 ID。
    /// </summary>
    [Credential(FlowConstants.CredentialFields.ApiKey)]
    [Description("Credential ID for search engine API key.")]
    public string? ApiKeyCredentialId { get; set; }

    /// <summary>
    /// 搜索语言。
    /// </summary>
    [Description("Search language (e.g. 'en', 'zh-CN').")]
    public string Language { get; set; } = "en";

    /// <summary>
    /// 最大结果数。
    /// </summary>
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
        try
        {
            // Get search query from LLM input
            var query = GetSearchQuery(context);
            if (string.IsNullOrWhiteSpace(query))
            {
                return context.ErrorResult("MissingQuery", "Search query is required.");
            }

            // Get API key
            var apiKey = await GetApiKeyAsync(context, cancellationToken).ConfigureAwait(false);

            // Execute search based on engine type
            var results = SearchEngine switch
            {
                SearchEngineType.Google => await SearchGoogleAsync(query, apiKey, cancellationToken).ConfigureAwait(false),
                SearchEngineType.Bing => await SearchBingAsync(query, apiKey, cancellationToken).ConfigureAwait(false),
                SearchEngineType.DuckDuckGo => await SearchDuckDuckGoAsync(query, cancellationToken).ConfigureAwait(false),
                SearchEngineType.SerpAPI => await SearchSerpApiAsync(query, apiKey, cancellationToken).ConfigureAwait(false),
                SearchEngineType.Custom => await SearchCustomAsync(query, apiKey, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported search engine: {SearchEngine}")
            };

            // Return results
            var outputBatch = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = results,
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            };

            return new NodeExecutionResult
            {
                Success = true,
                Output = outputBatch
            };
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult("Cancelled", "Search was cancelled.");
        }
        catch (Exception ex)
        {
            return context.ErrorResult("SearchFailed", $"Search failed: {ex.Message}");
        }
    }

    private string? GetSearchQuery(NodeExecutionContext context)
    {
        if (context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) && batch.Items.Count > 0)
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
        if (string.IsNullOrEmpty(ApiKeyCredentialId))
        {
            return null;
        }

        if (!Guid.TryParse(ApiKeyCredentialId, out var credentialId))
        {
            return null;
        }

        try
        {
            var credential = await context.Credentials.GetCredentialAsync(credentialId, cancellationToken)
                .ConfigureAwait(false);

            if (credential.Fields.TryGetValue(FlowConstants.CredentialFields.ApiKey, out var apiKey))
            {
                return apiKey;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<JsonNode?> SearchGoogleAsync(string query, string? apiKey, CancellationToken cancellationToken)
    {
        // Google Custom Search API
        var url = $"https://www.googleapis.com/customsearch/v1?key={apiKey}&cx={apiKey}&q={Uri.EscapeDataString(query)}&num={MaxResults}&hl={Language}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await HttpExecutionHelper.SendAndBuildResultAsync(request, Guid.Empty, cancellationToken)
            .ConfigureAwait(false);

        return response.Output.Items.FirstOrDefault()?.Data;
    }

    private async Task<JsonNode?> SearchBingAsync(string query, string? apiKey, CancellationToken cancellationToken)
    {
        // Bing Web Search API
        var url = $"https://api.bing.microsoft.com/v7.0/search?q={Uri.EscapeDataString(query)}&count={MaxResults}&setLang={Language}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", apiKey);
        }

        var response = await HttpExecutionHelper.SendAndBuildResultAsync(request, Guid.Empty, cancellationToken)
            .ConfigureAwait(false);

        return response.Output.Items.FirstOrDefault()?.Data;
    }

    private async Task<JsonNode?> SearchDuckDuckGoAsync(string query, CancellationToken cancellationToken)
    {
        // DuckDuckGo Instant Answer API (no API key required)
        var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await HttpExecutionHelper.SendAndBuildResultAsync(request, Guid.Empty, cancellationToken)
            .ConfigureAwait(false);

        return response.Output.Items.FirstOrDefault()?.Data;
    }

    private async Task<JsonNode?> SearchSerpApiAsync(string query, string? apiKey, CancellationToken cancellationToken)
    {
        // SerpAPI (Google Search)
        var url = $"https://serpapi.com/search.json?q={Uri.EscapeDataString(query)}&api_key={apiKey}&hl={Language}&num={MaxResults}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await HttpExecutionHelper.SendAndBuildResultAsync(request, Guid.Empty, cancellationToken)
            .ConfigureAwait(false);

        return response.Output.Items.FirstOrDefault()?.Data;
    }

    private async Task<JsonNode?> SearchCustomAsync(string query, string? apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(CustomEndpoint))
        {
            throw new InvalidOperationException("CustomEndpoint is required for Custom search engine.");
        }

        var url = CustomEndpoint.Replace("{query}", Uri.EscapeDataString(query))
                                .Replace("{language}", Language)
                                .Replace("{maxResults}", MaxResults.ToString());

        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (CustomHeaders is { Count: > 0 })
        {
            foreach (var (key, value) in CustomHeaders)
            {
                var resolvedValue = value.Replace("{apiKey}", apiKey ?? string.Empty);
                request.Headers.TryAddWithoutValidation(key, resolvedValue);
            }
        }

        var response = await HttpExecutionHelper.SendAndBuildResultAsync(request, Guid.Empty, cancellationToken)
            .ConfigureAwait(false);

        return response.Output.Items.FirstOrDefault()?.Data;
    }
}
