using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Http;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Options;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// HTTP 工具节点，作为 Agent 的工具被调用。
/// 支持静态配置（method、URL、authentication）和占位符机制。
/// </summary>
public sealed class HttpToolNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "httpTool";

    /// <inheritdoc />
    public string DisplayName => "HTTP Tool";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "globe";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// HTTP 方法。
    /// </summary>
    [Description("HTTP request method.")]
    public HttpMethodOption Method { get; set; } = HttpMethodOption.Get;

    /// <summary>
    /// 目标 URL，JS 表达式，返回字符串。
    /// </summary>
    [DisplayName("URL")]
    [Description("URL expression. Must return a string. Example: 'https://api.com/' + input.path")]
    [Hint(PresentationHint.Expression)]
    public Script Url { get; set; } = Script.Empty;

    /// <summary>
    /// 认证方式。
    /// </summary>
    [Description("Authentication method.")]
    public HttpRequestAuthMode Authentication { get; set; } = HttpRequestAuthMode.None;

    /// <summary>
    /// 凭据 ID。
    /// </summary>
    [Credential(FlowConstants.CredentialFields.ApiKey)]
    [Description("Credential ID for authentication.")]
    public string? CredentialId { get; set; }

    /// <summary>
    /// 是否发送自定义请求头。
    /// </summary>
    [DisplayName("Send Headers")]
    [Description("Whether to send custom headers.")]
    public bool SendHeaders { get; set; } = false;

    /// <summary>
    /// 请求头，JS 脚本，返回对象。
    /// </summary>
    [DisplayName("Headers")]
    [Description("Headers script. Must return an object. Example: { 'Authorization': 'Bearer ' + input.token }")]
    [Hint(PresentationHint.Script)]
    [DisplayCondition(nameof(SendHeaders), true)]
    public Script? HeadersExpression { get; set; }

    /// <summary>
    /// 是否发送请求体（仅 POST/PUT/PATCH 时显示）。
    /// </summary>
    [DisplayName("Send Body")]
    [Description("Whether to send a request body.")]
    [DisplayCondition(nameof(Method), HttpMethodOption.Post)]
    [DisplayCondition(nameof(Method), HttpMethodOption.Put)]
    [DisplayCondition(nameof(Method), HttpMethodOption.Patch)]
    public bool SendBody { get; set; } = false;

    /// <summary>
    /// 请求体，JS 脚本，返回对象。
    /// </summary>
    [DisplayName("Body")]
    [Description("Body script. Must return an object. Example: { name: input.name, count: input.count }")]
    [Hint(PresentationHint.Script)]
    [DisplayCondition(nameof(SendBody), true)]
    public Script? BodyExpression { get; set; }

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
            if (Url is null || string.IsNullOrWhiteSpace(Url.Source))
            {
                return context.ErrorResult("MissingUrl", "URL is required.");
            }

            var resolvedUrl = Url.GetResult<string>();
            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                return context.ErrorResult("MissingUrl", "URL resolution failed.");
            }

            if (SsrfGuard.IsInternalTarget(resolvedUrl))
            {
                return context.ErrorResult("SsrfBlocked", "Target URL points to a blocked internal/loopback address.");
            }

            var methodStr = Method.ToString().ToUpperInvariant();
            using var request = new HttpRequestMessage(new HttpMethod(methodStr), resolvedUrl);

            // Add authentication
            if (Authentication != HttpRequestAuthMode.None && !string.IsNullOrEmpty(CredentialId))
            {
                var credentialValue = await context.ResolveApiKeyAsync(CredentialId, cancellationToken).ConfigureAwait(false);
                if (credentialValue is not null)
                {
                    switch (Authentication)
                    {
                        case HttpRequestAuthMode.BearerToken:
                            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {credentialValue}");
                            break;
                        case HttpRequestAuthMode.ApiKey:
                            request.Headers.TryAddWithoutValidation("X-API-Key", credentialValue);
                            break;
                        case HttpRequestAuthMode.BasicAuth:
                            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentialValue));
                            request.Headers.TryAddWithoutValidation("Authorization", $"Basic {base64}");
                            break;
                    }
                }
            }

            var scriptCache = context.ScriptCache ?? new ScriptCache(Options.Create(new JsEngineOptions()));
            var scriptContext = ScriptContext.From(context);

            var preparedHeaders = SendHeaders && HeadersExpression is not null && !string.IsNullOrWhiteSpace(HeadersExpression.Source)
                ? scriptCache.GetOrPrepare(HeadersExpression)
                : null;
            var preparedBody = SendBody && BodyExpression is not null && !string.IsNullOrWhiteSpace(BodyExpression.Source) &&
                Method is HttpMethodOption.Post or HttpMethodOption.Put or HttpMethodOption.Patch
                ? scriptCache.GetOrPrepare(BodyExpression)
                : null;

            if (preparedHeaders is not null || preparedBody is not null)
            {
                using var session = (preparedHeaders ?? preparedBody)!.CreateSession(JsEngine.Create());

                if (preparedHeaders is not null)
                {
                    var headersResult = await session.RunAsync(preparedHeaders, scriptContext, cancellationToken).ConfigureAwait(false);
                    var headers = headersResult.To<Dictionary<string, string>>();
                    if (headers is not null)
                    {
                        foreach (var (key, value) in headers)
                        {
                            request.Headers.TryAddWithoutValidation(key, value);
                        }
                    }
                }

                if (preparedBody is not null)
                {
                    var bodyResult = await session.RunAsync(preparedBody, scriptContext, cancellationToken).ConfigureAwait(false);
                    var bodyJson = bodyResult.ToJson()?.ToJsonString() ?? string.Empty;
                    request.Content = new StringContent(bodyJson, Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));
                }
            }

            var client = context.HttpClientPool?.GetClient();
            if (client is null)
            {
                return context.ErrorResult("HttpClientUnavailable", "HTTP client pool is not configured.");
            }

            return await HttpExecutionHelper.SendAndBuildResultAsync(client, request, context.Node.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult("Cancelled", "HTTP request was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return context.ErrorResult("HttpRequestFailed", $"HTTP request failed: {ex.Message}");
        }
        catch (ScriptErrorException ex)
        {
            return context.ErrorResult("ScriptError", $"Script evaluation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult("UnexpectedError", $"Unexpected HTTP error: {ex.Message}");
        }
    }

}

/// <summary>
/// HTTP 占位符定义。
/// </summary>
public sealed class HttpPlaceholder
{
    /// <summary>
    /// 占位符名称（对应 URL/Body 中的 {name}）。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 占位符描述（帮助 LLM 理解需要什么值）。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否必填。
    /// </summary>
    public bool Required { get; set; } = true;
}
