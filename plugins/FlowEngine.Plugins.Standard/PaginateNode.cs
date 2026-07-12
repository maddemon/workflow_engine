using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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
/// 游标分页请求节点。通过 <c>$cursor</c>/<c>$nextCursor</c>/<c>$page</c>/<c>$response</c>
/// 在每轮迭代中本地注入（plan-004：节点私有变量不注册到顶层全局），按
/// <c>nextCursorPath</c> 提取下一页游标，按 <c>terminateWhen</c> 表达式判断是否终止，
/// 将各页 <c>itemsPath</c> 下的数组合并为单一输出。
/// </summary>
public sealed class PaginateNode : INodeType
{
    private static readonly IHttpExecutionService s_httpService = new HttpExecutionService();

    /// <inheritdoc />
    public string TypeName => "paginate";

    /// <inheritdoc />
    public string DisplayName => "Paginate (Cursor)";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "repeat";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>HTTP 请求方法。</summary>
    [Description("HTTP request method.")]
    public HttpMethodOption Method { get; set; } = HttpMethodOption.Get;

    /// <summary>认证方式（可选，draft 通常通过 URL 内嵌 token）。</summary>
    [Description("Authentication method.")]
    public HttpRequestAuthMode Authentication { get; set; } = HttpRequestAuthMode.None;

    /// <summary>起始游标（字符串）；实际类型由 <see cref="CursorType"/> 决定。</summary>
    [Description("Initial cursor value.")]
    public string? CursorInitial { get; set; } = "0";

    /// <summary>游标类型：number 或 string。</summary>
    [Description("Cursor type: 'number' or 'string'.")]
    public string CursorType { get; set; } = "string";

    /// <summary>最大分页次数（安全上限，防止无限循环）。</summary>
    [Description("Maximum number of pages to fetch (safety cap).")]
    public int MaxPages { get; set; } = 100;

    /// <summary>
    /// 业务成功判定表达式。HTTP 2xx 后按此表达式判定业务是否成功（如 <c>$json.errcode == 0</c>）。
    /// </summary>
    [DisplayName("Success When")]
    [Description("Business success condition. When set, even a 2xx HTTP response fails the node if this expression evaluates to false (e.g. '$json.errcode == 0').")]
    [Hint(PresentationHint.Expression)]
    public Script SuccessWhen { get; set; } = Script.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.ContextFactory is null || context.NodeRegistry is null)
        {
            return context.ErrorResult("ContextFactoryMissing", "PaginateNode requires a context factory to iterate.");
        }

        var cursorType = GetConfig(context, "cursorType", "string");
        var nextCursorPath = GetConfig(context, "nextCursorPath", "");
        var itemsPath = GetConfig(context, "itemsPath", "");
        var terminateWhen = GetConfig(context, "terminateWhen", "$nextCursor == ''");
        var credentialName = GetConfig(context, "credentialName", "");
        var maxPages = int.TryParse(GetConfig(context, "maxPages", "100"), out var mp) && mp > 0 ? mp : 100;

        var nodeType = context.NodeRegistry.Get(context.Node.TypeName);
        var execution = new ExecutionRecord { Id = context.ExecutionId };

        object? cursor = CoerceCursorLiteral(GetConfig(context, "cursorInitial", "0"), cursorType);
        JsonNode? lastResponse = null;
        var allItems = new List<DataItem>();

        var terminateScript = new Script
        {
            Source = terminateWhen,
            Language = ScriptLanguage.JavaScript,
            ReturnType = ScriptReturnType.Bool
        };

        for (var page = 0; page < maxPages; page++)
        {
            var extraGlobals = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["$cursor"] = cursor,
                ["$nextCursor"] = cursor,
                ["$page"] = page,
                ["$response"] = lastResponse
            };

            NodeExecutionContext iterContext;
            try
            {
                iterContext = await context.ContextFactory.CreateAsync(
                    context.Workflow,
                    execution,
                    context.Node,
                    nodeType,
                    context.Inputs,
                    new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase),
                    context.RunIndex,
                    cancellationToken,
                    credentialAccessorOverride: context.Credentials,
                    extraGlobals: extraGlobals).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return context.ErrorResult("PageResolveFailed", $"Failed to resolve page {page}: {ex.Message}");
            }

            var resolved = iterContext.ResolvedParameters;
            var resolvedUrl = resolved.TryGetValue("url", out var ru) ? ru as string : null;
            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingUrl, $"URL resolution failed on page {page}.");
            }

            var httpMethod = ResolveMethod(resolved, Method);

            string? bodyJson = null;
            if (httpMethod != HttpMethod.Get && httpMethod != HttpMethod.Head
                && resolved.TryGetValue("bodyExpression", out var rb) && rb is not null)
            {
                bodyJson = BuildBody(rb);
            }

            var httpRequest = new HttpExecutionRequest
            {
                Url = resolvedUrl,
                Method = httpMethod,
                AuthMode = Authentication,
                CredentialId = credentialName,
                BodyContent = bodyJson
            };

            NodeExecutionResult pageResult;
            try
            {
                pageResult = await s_httpService.ExecuteAsync(httpRequest, context, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.Cancelled, "Paginated request was cancelled.");
            }
            catch (HttpRequestException ex)
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.HttpRequestFailed, $"HTTP request failed on page {page}: {ex.Message}");
            }

            if (!pageResult.Success)
            {
                return new NodeExecutionResult
                {
                    Success = false,
                    Output = new DataBatch { Items = allItems },
                    Error = pageResult.Error
                };
            }

            // 阶段零 0.2：HTTP 成功后判 successWhen 业务成功表达式（如钉钉 errcode != 0 但 HTTP 200）
            var successWhenExpr = GetSuccessWhenExpression();
            if (!string.IsNullOrWhiteSpace(successWhenExpr))
            {
                var envelope = pageResult.Output.Items.Count > 0 ? pageResult.Output.Items[0].Data as JsonObject : null;
                var body = envelope?["body"];
                var statusCode = envelope?["statusCode"]?.GetValue<int>() ?? 200;
                var statusText = envelope?["statusText"]?.GetValue<string>();
                var businessOk = await HttpExecutionHelper.EvaluateSuccessWhenAsync(
                    new Script { Source = successWhenExpr, Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.Bool },
                    body,
                    statusCode,
                    statusText,
                    context,
                    cancellationToken).ConfigureAwait(false);
                if (!businessOk)
                {
                    var errcode = body?["errcode"]?.GetValue<int>();
                    var errmsg = body?["errmsg"]?.GetValue<string>();
                    var subMsg = body?["sub_msg"]?.GetValue<string>();
                    var detail = errcode.HasValue ? $"，实际 errcode={errcode}" : "";
                    if (!string.IsNullOrEmpty(subMsg))
                        detail += $"，{subMsg}";
                    else if (!string.IsNullOrEmpty(errmsg))
                        detail += $"，{errmsg}";
                    return context.ErrorResult(FlowConstants.ErrorCodes.SuccessWhenFailed,
                        $"业务条件未满足：{successWhenExpr}{detail}");
                }
            }

            var responseBody = pageResult.Output.Items.Count > 0 ? pageResult.Output.Items[0].Data : null;

            // HTTP 响应体位于输出信封的 .body 下
            var httpBody = responseBody is JsonObject env && env["body"] is JsonNode b ? b : responseBody;

            // 提取本页数据项
            if (!string.IsNullOrEmpty(itemsPath) && httpBody is JsonNode bodyNode)
            {
                var itemsNode = Navigate(bodyNode, itemsPath);
                if (itemsNode is JsonArray arr)
                {
                    foreach (var item in arr)
                    {
                        allItems.Add(new DataItem
                        {
                            Data = item?.DeepClone(),
                            Success = true,
                            SourceIndex = allItems.Count
                        });
                    }
                }
            }

            // 提取下一页游标
            object? nextCursor = null;
            if (!string.IsNullOrEmpty(nextCursorPath) && httpBody is JsonNode nextNode)
            {
                nextCursor = CoerceCursor(Navigate(nextNode, nextCursorPath), cursorType);
            }

            // 终止判断：以新游标作为 $nextCursor 求值 terminateWhen
            bool stop;
            try
            {
                stop = await terminateScript.EvaluateAsync<bool>(context,
                    cancellationToken,
                    ("$cursor", cursor),
                    ("$nextCursor", nextCursor),
                    ("$page", page),
                    ("$response", httpBody)).ConfigureAwait(false);
            }
            catch (ScriptErrorException)
            {
                stop = false;
            }

            if (stop)
            {
                break;
            }

            // 安全兜底：游标为空表示无更多数据
            if (nextCursor is null || (nextCursor is string s && s == ""))
            {
                break;
            }

            lastResponse = responseBody;
            cursor = nextCursor;
        }

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = allItems }
        };
    }

    private string GetSuccessWhenExpression()
    {
        if (SuccessWhen is not null && !string.IsNullOrWhiteSpace(SuccessWhen.Source))
        {
            return SuccessWhen.Source;
        }

        return string.Empty;
    }

    private static string GetConfig(NodeExecutionContext context, string key, string fallback)
    {
        if (context.RawParameters.TryGetValue(key, out var v) && v is string s && !string.IsNullOrEmpty(s))
        {
            return s;
        }

        return fallback;
    }

    private static HttpMethod ResolveMethod(IReadOnlyDictionary<string, object> resolved, HttpMethodOption fallback)
    {
        if (resolved.TryGetValue("method", out var m) && m is string ms && !string.IsNullOrWhiteSpace(ms))
        {
            return new HttpMethod(ms.ToUpperInvariant());
        }

        return new HttpMethod(fallback.ToString().ToUpperInvariant());
    }

    private static string? BuildBody(object? resolvedBody)
    {
        return resolvedBody switch
        {
            JsonObject jo => jo.ToJsonString(),
            JsonNode jn => jn.ToJsonString(),
            string s => s,
            null => null,
            _ => resolvedBody.ToString()
        };
    }

    private static object? CoerceCursorLiteral(string literal, string cursorType)
    {
        if (string.IsNullOrEmpty(literal))
        {
            return cursorType.Equals("number", StringComparison.OrdinalIgnoreCase) ? 0 : "";
        }

        if (cursorType.Equals("number", StringComparison.OrdinalIgnoreCase) && int.TryParse(literal, out var i))
        {
            return i;
        }

        return literal;
    }

    private static object? CoerceCursor(JsonNode? node, string cursorType)
    {
        if (node is not JsonValue val)
        {
            return null;
        }

        if (cursorType.Equals("number", StringComparison.OrdinalIgnoreCase))
        {
            if (val.TryGetValue(out int i)) return i;
            if (val.TryGetValue(out long l)) return l;
            if (val.TryGetValue(out double d)) return (long)d;
        }

        if (val.TryGetValue(out string? s))
        {
            return s;
        }

        var str = node.ToString();
        if (cursorType.Equals("number", StringComparison.OrdinalIgnoreCase) && int.TryParse(str, out var ni))
        {
            return ni;
        }

        return str;
    }

    private static JsonNode? Navigate(JsonNode? node, string path)
    {
        if (node is null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var current = node;
        var remaining = path.AsSpan().Trim();

        while (remaining.Length > 0)
        {
            remaining = remaining.TrimStart('.');
            if (remaining.Length == 0) break;

            if (remaining[0] == '[')
            {
                var end = remaining.IndexOf(']');
                if (end < 0) return null;

                if (current is JsonArray arr && int.TryParse(remaining[1..end].ToString(), out var idx) && idx >= 0 && idx < arr.Count)
                {
                    current = arr[idx];
                }
                else
                {
                    return null;
                }

                remaining = remaining[(end + 1)..];
                continue;
            }

            var firstSpecial = -1;
            for (var i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] == '.' || remaining[i] == '[')
                {
                    firstSpecial = i;
                    break;
                }
            }

            if (firstSpecial < 0) firstSpecial = remaining.Length;

            var key = remaining[..firstSpecial].ToString();
            if (current is JsonObject obj && obj.TryGetPropertyValue(key, out var child))
            {
                current = child;
            }
            else
            {
                return null;
            }

            remaining = remaining[firstSpecial..];
        }

        return current;
    }
}
