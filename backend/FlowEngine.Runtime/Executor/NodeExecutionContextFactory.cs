using System;
using System.Security.Cryptography;
using System.Text.Json;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Expressions;
using FlowEngine.Runtime.Credentials;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 构造节点执行上下文。
/// </summary>
public sealed class NodeExecutionContextFactory(
    INodeRegistry registry,
    ParameterResolver parameterResolver,
    ICredentialAccessor credentialAccessor,
    IReadOnlySet<string> environmentWhitelist,
    ILogger<ParameterHydrator>? hydratorLogger = null,
    ILogger<JsEngine>? jsLogger = null,
    JsEngineOptions? jsEngineOptions = null,
    ILlmClient? llmClient = null,
    IWorkflowLoader? workflowLoader = null,
    IHttpClientPool? httpClientPool = null,
    IOAuth2TokenService? tokenService = null) : Core.Abstractions.INodeExecutionContextFactory
{
    private readonly IOAuth2TokenService? _tokenService = tokenService;

    public async Task<NodeExecutionContext> CreateAsync(
        Workflow workflow,
        ExecutionRecord execution,
        NodeDefinition node,
        INodeType nodeInstance,
        IReadOnlyDictionary<string, DataBatch> inputs,
        IReadOnlyDictionary<string, DataBatch> successfulOutputs,
        IReadOnlyDictionary<string, DataBatch> latestBatches,
        int runIndex,
        CancellationToken cancellationToken,
        ICredentialAccessor? credentialAccessorOverride = null,
        IReadOnlyDictionary<string, object?>? extraGlobals = null)
    {
        var nodeDefinition = node;
        var descriptor = registry.GetDescriptor(node.TypeName);
        var rawParameters = MergeParameters(nodeDefinition, descriptor);
        var cacheKey = BuildCacheKey(descriptor);

        // CodeEditor/Script 参数由节点自己执行，跳过表达式求值
        var codeParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in descriptor.Parameters)
        {
            if (p.Hint is PresentationHint.CodeEditor or PresentationHint.Script)
            {
                codeParamNames.Add(p.Name);
            }
        }

        var rawCodeParams = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (codeParamNames.Count > 0)
        {
            foreach (var name in codeParamNames)
            {
                if (rawParameters.Remove(name, out var val))
                {
                    rawCodeParams[name] = val;
                }
            }
        }

        using var js = JsEngine.Create(options: jsEngineOptions, logger: jsLogger);

        // --- 旧式裸名全局（plan-004 迁移期向后兼容） ---
        var currentInput = GetCurrentInput(inputs, runIndex);
        js.SetValue("input", currentInput);
        js.SetValue("inputs", inputs);
        js.SetValue("parameter", rawParameters);
        js.SetValue("nodes", successfulOutputs);
        js.SetValue("items", latestBatches);
        js.SetValue("workflow", new Dictionary<string, object?>
        {
            ["id"] = workflow.Id,
            ["name"] = workflow.Name,
            ["projectId"] = workflow.ProjectId,
            ["version"] = workflow.Version,
            ["isActive"] = workflow.IsActive,
        });
        js.SetValue("execution", new Dictionary<string, object?>
        {
            ["id"] = execution.Id,
        });
        js.SetValue("runIndex", runIndex);
        js.SetValue("run_index", runIndex);
        js.SetValue("env", new EnvironmentAccessor(environmentWhitelist));
        js.SetValue("now", DateTime.UtcNow);

        // --- $ 前缀内建变量（plan-004 评审5：$ 前缀 = 引擎内建，裸名 = 用户数据） ---

        // $json：当前 item 数据（等价于旧 input，但带 $ 前缀避免与用户字段冲突）
        js.SetValue("$json", currentInput);

        // $input：n8n 式输入容器
        var inputItems = GetInputItemList(inputs);
        var inputContext = new Dictionary<string, object?>
        {
            ["executionId"] = execution.Id,
            ["runIndex"] = runIndex,
            ["nodeName"] = node.Name,
            ["nodeType"] = node.TypeName,
            ["workflowId"] = workflow.Id,
        };
        var inputContainer = new InputContainer(inputItems, currentInput, rawParameters, inputContext);
        js.SetValue("$input", inputContainer);

        // $items(name?)：获取指定节点全部 item；无参时等价 $input.All()
        js.SetValue("$items", new Func<string?, object?>(nodeName =>
        {
            if (string.IsNullOrEmpty(nodeName))
                return inputItems;
            if (latestBatches.TryGetValue(nodeName, out var batch))
                return batch.Items.Select(i => (object?)i.Data).ToList();
            return null;
        }));

        // $node['Name']：指定节点输出（含 .json / .params / .context / .runIndex）
        // 注：successfulOutputs 仅有 DataBatch，params/context 需跨节点存储扩展字段；当前只填 .json
        var nodeDict = new Dictionary<string, NodeOutput>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, batch) in successfulOutputs)
        {
            nodeDict[name] = new NodeOutput(
                batch.Items.Select(i => (object?)i.Data).ToList());
        }
        js.SetValue("$node", nodeDict);

        // $workflow / $execution（复用旧式对象，加 $ 前缀注入）
        js.SetValue("$workflow", new Dictionary<string, object?>
        {
            ["id"] = workflow.Id,
            ["name"] = workflow.Name,
            ["projectId"] = workflow.ProjectId,
            ["version"] = workflow.Version,
            ["isActive"] = workflow.IsActive,
        });
        js.SetValue("$execution", new Dictionary<string, object?>
        {
            ["id"] = execution.Id,
        });

        // $env / $vars（vars = 可写工作流级状态，暂为空对象）
        js.SetValue("$env", new EnvironmentAccessor(environmentWhitelist));
        js.SetValue("$vars", new Dictionary<string, object?>());

        // $now / $today
        js.SetValue("$now", DateTime.UtcNow);
        js.SetValue("$today", DateTime.UtcNow.Date);

        // $runIndex / $itemIndex（itemIndex 与 runIndex 在当前上下文中一致）
        js.SetValue("$runIndex", runIndex);
        js.SetValue("$itemIndex", runIndex);

        // $credentials：多字段凭据容器，支持属性式访问 $credentials.<name>.<field>
        var credsAccessor = credentialAccessorOverride ?? credentialAccessor;
        if (_tokenService is not null)
        {
            credsAccessor = new OAuth2CredentialAccessor(credsAccessor, _tokenService);
        }
        var credentialsDict = await PreloadCredentialsAsync(rawParameters, credsAccessor, cancellationToken)
            .ConfigureAwait(false);
        js.SetValue("$credentials", credentialsDict);

        // $ctx：上下文 bundle 自身（与函数式 ctx => 的 ctx 参数等价）。
        // 各字段已单独设为 $ 全局，此处作为集合容器注入。函数式：function(ctx){ return ctx.$json; }
        js.SetValue("$ctx", new Dictionary<string, object?>
        {
            ["$json"] = currentInput,
            ["$input"] = inputContainer,
            ["$node"] = nodeDict,
            ["$runIndex"] = runIndex,
            ["$itemIndex"] = runIndex,
            ["$credentials"] = credentialsDict,
            ["parameter"] = rawParameters,
        });

        // 节点私有全局（plan-004：由各自节点本地注入，工厂不感知具体变量名，避免顶层全局膨胀）。
        // 例如 PaginateNode 每轮迭代注入 $cursor/$nextCursor/$page/$response。
        if (extraGlobals is not null)
        {
            foreach (var (key, value) in extraGlobals)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    js.SetValue(key, value);
                }
            }
        }

        var resolvedParameters = parameterResolver.Resolve(rawParameters, js, cacheKey);

        // 将 CodeEditor/Script 参数原样放回
        foreach (var (name, val) in rawCodeParams)
        {
            resolvedParameters[name] = val;
        }

        var hydrator = new ParameterHydrator(credsAccessor, hydratorLogger);
        await hydrator.HydrateAsync(nodeInstance, resolvedParameters).ConfigureAwait(false);

        return new NodeExecutionContext
        {
            Workflow = workflow,
            ExecutionId = execution.Id,
            Node = nodeDefinition,
            RunIndex = runIndex,
            Inputs = inputs,
            RawParameters = rawParameters,
            ResolvedParameters = resolvedParameters,
            Credentials = credsAccessor,
            Logger = NullExecutionLogger.Instance,
            CancellationToken = cancellationToken,
            LlmClient = llmClient,
            HttpClientPool = httpClientPool,
            NodeRegistry = registry,
            ContextFactory = this,
            WorkflowLoader = workflowLoader,
        };
    }

    private static ExpressionCacheKey? BuildCacheKey(NodeTypeDescriptor descriptor)
    {
        var inputPort = descriptor.Ports.FirstOrDefault(p =>
            p.Name.Equals(FlowConstants.PortNames.Input, StringComparison.OrdinalIgnoreCase)
            && p.Direction == PortDirection.Input
            && p.Type == PortType.Main);

        var inputSchemaHash = ComputeHash(inputPort?.ExpectedSchema);
        var parameterSchemaHash = ComputeHash(descriptor.Parameters);

        if (string.IsNullOrEmpty(inputSchemaHash) && string.IsNullOrEmpty(parameterSchemaHash))
        {
            return null;
        }

        return new ExpressionCacheKey(string.Empty, inputSchemaHash, parameterSchemaHash);
    }

    private static string ComputeHash(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var json = JsonSerializer.Serialize(value, JsonDefaults.Options);
        if (string.IsNullOrEmpty(json) || json == "{}")
        {
            return string.Empty;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
#if NET8_0_OR_GREATER
        var hash = SHA256.HashData(bytes);
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
#endif
        return Convert.ToHexString(hash);
    }

    private static object? GetCurrentInput(IReadOnlyDictionary<string, DataBatch> inputs, int runIndex)
    {
        if (!inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) || batch.Items.Count == 0)
        {
            return null;
        }

        var index = runIndex >= 0 && runIndex < batch.Items.Count ? runIndex : 0;
        return batch.Items[index].Data;
    }

    /// <summary>
    /// 获取所有输入 item 的 Data 列表。用于 <c>$input.All()</c> 和 <c>$items()</c>。
    /// </summary>
    private static List<object?> GetInputItemList(IReadOnlyDictionary<string, DataBatch> inputs)
    {
        if (!inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) || batch.Items.Count == 0)
        {
            return [];
        }

        return batch.Items.Select(i => (object?)i.Data).ToList();
    }

    /// <summary>
    /// 从节点参数字符串值中提取 <c>$credentials.&lt;name&gt;</c> 引用中的凭据名称。
    /// </summary>
    private static HashSet<string> ExtractCredentialNames(IReadOnlyDictionary<string, object> parameters)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in parameters.Values)
        {
            if (value is string str)
            {
                var span = str.AsSpan();
                var dollarIdx = span.IndexOf("$credentials.", StringComparison.OrdinalIgnoreCase);
                while (dollarIdx >= 0)
                {
                    var after = dollarIdx + "$credentials.".Length;
                    var end = after;
                    while (end < span.Length && (char.IsLetterOrDigit(span[end]) || span[end] == '_'))
                        end++;
                    if (end > after)
                    {
                        names.Add(span[after..end].ToString());
                    }
                    var remaining = span[end..];
                    dollarIdx = remaining.IndexOf("$credentials.", StringComparison.OrdinalIgnoreCase);
                    if (dollarIdx >= 0)
                        dollarIdx += end;
                    else
                        dollarIdx = -1;
                }
            }
        }
        return names;
    }

    /// <summary>
    /// 预加载参数字典中引用的凭据，返回凭据名称 → 字段字典的映射。
    /// </summary>
    private static async Task<Dictionary<string, object?>> PreloadCredentialsAsync(
        IReadOnlyDictionary<string, object> rawParameters,
        ICredentialAccessor credsAccessor,
        CancellationToken ct)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var names = ExtractCredentialNames(rawParameters);
        if (names.Count == 0) return result;

        foreach (var name in names)
        {
            try
            {
                var cv = await credsAccessor.GetCredentialByNameAsync(name, ct).ConfigureAwait(false);
                if (cv is not null && cv.Fields.Count > 0)
                {
                    // 转为 Dictionary<string, object?> 以在 Jint 中支持属性式访问
                    result[name] = cv.Fields.ToDictionary(
                        kv => kv.Key, kv => (object?)kv.Value);
                }
            }
            catch
            {
                // 凭据加载失败时不阻断执行，跳过该凭据
            }
        }
        return result;
    }

    private static Dictionary<string, object> MergeParameters(
        NodeDefinition nodeDefinition,
        NodeTypeDescriptor descriptor)
    {
        var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in descriptor.Parameters)
        {
            if (nodeDefinition.Parameters.TryGetValue(parameter.Name, out var value))
            {
                merged[parameter.Name] = value;
            }
            else if (parameter.DefaultValue is not null)
            {
                merged[parameter.Name] = parameter.DefaultValue;
            }
        }

        foreach (var (key, value) in nodeDefinition.Parameters)
        {
            if (!merged.ContainsKey(key))
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    private sealed class NullExecutionLogger : IExecutionLogger
    {
        public static readonly NullExecutionLogger Instance = new();
        public void LogInformation(string message, params object?[] args) { }
        public void LogWarning(string message, params object?[] args) { }
        public void LogError(Exception? exception, string message, params object?[] args) { }
    }
}
