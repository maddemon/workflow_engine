using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
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
    IScriptCache scriptCache,
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

        // CodeEditor/Script 的非 Script 字符串参数仍由节点自己执行，先抽出避免被 ParameterResolver 误求值
        var codeParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in descriptor.Parameters)
        {
            if (p.Type != ParameterType.Script && p.Hint is PresentationHint.CodeEditor or PresentationHint.Script)
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

        var currentInput = GetCurrentInput(inputs, runIndex);
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

        var nodeDict = new Dictionary<string, NodeOutput>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, batch) in successfulOutputs)
        {
            nodeDict[name] = new NodeOutput(
                batch.Items.Select(i => (object?)i.Data).ToList());
        }

        var credsAccessor = credentialAccessorOverride ?? credentialAccessor;
        if (_tokenService is not null)
        {
            credsAccessor = new OAuth2CredentialAccessor(credsAccessor, _tokenService);
        }
        var credentialsDict = await PreloadCredentialsAsync(rawParameters, credsAccessor, hydratorLogger, cancellationToken)
            .ConfigureAwait(false);

        var workflowDict = new Dictionary<string, object?>
        {
            ["id"] = workflow.Id,
            ["name"] = workflow.Name,
            ["projectId"] = workflow.ProjectId,
            ["version"] = workflow.Version,
            ["isActive"] = workflow.IsActive,
        };
        var executionDict = new Dictionary<string, object?>
        {
            ["id"] = execution.Id,
        };
        var ctxDict = new Dictionary<string, object?>
        {
            ["$json"] = currentInput,
            ["$input"] = inputContainer,
            ["$node"] = nodeDict,
            ["$runIndex"] = runIndex,
            ["$itemIndex"] = runIndex,
            ["$credentials"] = credentialsDict,
            ["parameter"] = rawParameters,
        };

        // 统一全局变量表：既注入 JsEngine（供 ParameterResolver 与 Script 管线复用），
        // 也作为 ScriptContext.ExtraGlobals 传入，保证两种求值路径变量集一致。
        var globals = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            // 旧式裸名全局（plan-004 迁移期向后兼容）
            ["input"] = currentInput,
            ["inputs"] = inputs,
            ["parameter"] = rawParameters,
            ["nodes"] = successfulOutputs,
            ["items"] = latestBatches,
            ["workflow"] = workflowDict,
            ["execution"] = executionDict,
            ["runIndex"] = runIndex,
            ["run_index"] = runIndex,
            ["env"] = new EnvironmentAccessor(environmentWhitelist),
            ["now"] = DateTime.UtcNow,

            // $ 前缀内建变量（plan-004 评审5）
            ["$json"] = currentInput,
            ["$input"] = inputContainer,
            ["$items"] = new Func<string?, object?>(nodeName =>
            {
                if (string.IsNullOrEmpty(nodeName))
                    return inputItems;
                if (latestBatches.TryGetValue(nodeName, out var batch))
                    return batch.Items.Select(i => (object?)i.Data).ToList();
                return null;
            }),
            ["$node"] = nodeDict,
            ["$workflow"] = workflowDict,
            ["$execution"] = executionDict,
            ["$env"] = new EnvironmentAccessor(environmentWhitelist),
            ["$vars"] = new Dictionary<string, object?>(),
            ["$now"] = DateTime.UtcNow,
            ["$today"] = DateTime.UtcNow.Date,
            ["$runIndex"] = runIndex,
            ["$itemIndex"] = runIndex,
            ["$credentials"] = credentialsDict,
            ["$ctx"] = ctxDict,
        };

        foreach (var (key, value) in globals)
        {
            js.SetValue(key, value);
        }

        // 节点私有全局（plan-004：由各自节点本地注入，工厂不感知具体变量名，避免顶层全局膨胀）。
        if (extraGlobals is not null)
        {
            foreach (var (key, value) in extraGlobals)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    globals[key] = value;
                    js.SetValue(key, value);
                }
            }
        }

        // Script 类型参数预求值：Expression 脚本在 Hydrate 前完成求值并写入 ResolvedValue。
        var preEvalContext = new NodeExecutionContext
        {
            Workflow = workflow,
            ExecutionId = execution.Id,
            Node = nodeDefinition,
            RunIndex = runIndex,
            Inputs = inputs,
            RawParameters = rawParameters,
            Credentials = credsAccessor,
            CancellationToken = cancellationToken,
        };
        var scriptContext = new ScriptContext(preEvalContext, globals);
        await PreEvaluateScriptParametersAsync(rawParameters, descriptor, scriptContext, js, scriptCache, cancellationToken)
            .ConfigureAwait(false);

        var resolvedParameters = await parameterResolver.ResolveAsync(rawParameters, js, cancellationToken)
            .ConfigureAwait(false);

        // 将 CodeEditor/Script 字符串参数原样放回
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
            ScriptCache = scriptCache,
            GlobalVariables = BuildGlobalVariables(credentialsDict, workflow, execution.Id, nodeDefinition, runIndex, rawParameters, environmentWhitelist),
        };
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
        ILogger<ParameterHydrator>? hydratorLogger,
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
            catch (Exception ex)
            {
                // 凭据加载失败时不阻断执行，记录警告后跳过该凭据
                hydratorLogger?.LogWarning(ex, "预加载凭据 '{CredentialName}' 失败：{ErrorMessage}", name, ex.Message);
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

    /// <summary>
    /// 对 Script 类型参数执行预求值：Expression 脚本直接求值并写入 ResolvedValue，
    /// Script/CodeEditor 脚本保持原样；递归处理 Dictionary&lt;string, Script&gt;。
    /// </summary>
    private static async Task PreEvaluateScriptParametersAsync(
        Dictionary<string, object> rawParameters,
        NodeTypeDescriptor descriptor,
        ScriptContext scriptContext,
        JsEngine js,
        IScriptCache scriptCache,
        CancellationToken cancellationToken)
    {
        foreach (var (name, value) in rawParameters.ToList())
        {
            var definition = descriptor.Parameters.FirstOrDefault(
                p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (value is Script script)
            {
                if (definition?.Hint == PresentationHint.Expression)
                {
                    var expressionResult = await EvaluateScriptAsync(script, scriptContext, js, scriptCache, cancellationToken)
                        .ConfigureAwait(false);
                    if (!expressionResult.Success)
                    {
                        throw new ScriptErrorException(script, $"参数表达式预求值失败: {expressionResult.Error?.Reason}", expressionResult.Error);
                    }

                    rawParameters[name] = script.WithResolvedValue(expressionResult.ToJson());
                }

                continue;
            }

            if (TryConvertToDictionaryOfScript(value, out var dict) && dict is not null)
            {
                if (definition?.Hint == PresentationHint.Expression)
                {
                    var evaluated = new Dictionary<string, Script>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (key, itemScript) in dict)
                    {
                        var itemResult = await EvaluateScriptAsync(itemScript, scriptContext, js, scriptCache, cancellationToken)
                            .ConfigureAwait(false);
                        if (!itemResult.Success)
                        {
                            throw new ScriptErrorException(itemScript, $"列映射表达式预求值失败: {itemResult.Error?.Reason}", itemResult.Error);
                        }

                        evaluated[key] = itemScript.WithResolvedValue(itemResult.ToJson());
                    }

                    rawParameters[name] = evaluated;
                }
                else
                {
                    rawParameters[name] = dict;
                }

                continue;
            }

            if (definition?.Type == ParameterType.Script)
            {
                var converted = ConvertToScript(value);
                if (converted is null)
                {
                    continue;
                }

                if (definition.Hint == PresentationHint.Expression)
                {
                    var expressionResult = await EvaluateScriptAsync(converted, scriptContext, js, scriptCache, cancellationToken)
                        .ConfigureAwait(false);
                    if (!expressionResult.Success)
                    {
                        throw new ScriptErrorException(converted, $"参数表达式预求值失败: {expressionResult.Error?.Reason}", expressionResult.Error);
                    }

                    converted = converted.WithResolvedValue(expressionResult.ToJson());
                }

                rawParameters[name] = converted;
            }
        }
    }

    private static async Task<ScriptResult> EvaluateScriptAsync(
        Script script,
        ScriptContext context,
        JsEngine js,
        IScriptCache scriptCache,
        CancellationToken cancellationToken)
    {
        var prepared = scriptCache.GetOrPrepare(script);
        return await prepared.RunAsync(context, js, cancellationToken).ConfigureAwait(false);
    }



    private static Script? ConvertToScript(object? value)
    {
        return value switch
        {
            Script s => s,
            string str => new Script { Source = str, Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.String },
            JsonElement element => element.Deserialize<Script>(JsonDefaults.Options),
            JsonNode node => node.Deserialize<Script>(JsonDefaults.Options),
            _ => null
        };
    }

    private static bool TryConvertToDictionaryOfScript(object? value, out Dictionary<string, Script>? dict)
    {
        if (value is Dictionary<string, Script> d)
        {
            dict = d;
            return true;
        }

        try
        {
            if (value is JsonElement element)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    dict = null;
                    return false;
                }

                dict = element.Deserialize<Dictionary<string, Script>>(JsonDefaults.Options);
                return dict is not null;
            }

            if (value is JsonObject obj)
            {
                dict = obj.Deserialize<Dictionary<string, Script>>(JsonDefaults.Options);
                return dict is not null;
            }

            if (value is JsonNode node)
            {
                if (node is not JsonObject)
                {
                    dict = null;
                    return false;
                }

                dict = node.Deserialize<Dictionary<string, Script>>(JsonDefaults.Options);
                return dict is not null;
            }

            if (value is string str)
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, Script>>(str, JsonDefaults.Options);
                return dict is not null;
            }
        }
        catch (JsonException)
        {
            // 值不是 Dictionary<string, Script> 结构，返回 false 让后续分支处理
        }

        dict = null;
        return false;
    }

    /// <summary>
    /// 构建非逐项全局变量字典，供节点在逐项求值时复用。
    /// 不含 $json/$input/$itemIndex/$runIndex（逐项变量由各节点自行注入）。
    /// </summary>
    private static Dictionary<string, object?> BuildGlobalVariables(
        Dictionary<string, object?> credentialsDict,
        Workflow workflow,
        Guid executionId,
        NodeDefinition nodeDefinition,
        int runIndex,
        IReadOnlyDictionary<string, object> rawParameters,
        IReadOnlySet<string> environmentWhitelist)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["$credentials"] = credentialsDict,
            ["$workflow"] = new Dictionary<string, object?>
            {
                ["id"] = workflow.Id,
                ["name"] = workflow.Name,
                ["projectId"] = workflow.ProjectId,
                ["version"] = workflow.Version,
                ["isActive"] = workflow.IsActive,
            },
            ["$execution"] = new Dictionary<string, object?>
            {
                ["id"] = executionId,
            },
            ["$env"] = new EnvironmentAccessor(environmentWhitelist),
            ["$vars"] = new Dictionary<string, object?>(),
            ["$now"] = DateTime.UtcNow,
            ["$today"] = DateTime.UtcNow.Date,
            ["$node"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            ["$ctx"] = new Dictionary<string, object?>
            {
                ["$credentials"] = credentialsDict,
                ["parameter"] = rawParameters,
            },
            ["parameter"] = rawParameters,
        };
    }

    private sealed class NullExecutionLogger : IExecutionLogger
    {
        public static readonly NullExecutionLogger Instance = new();
        public void LogInformation(string message, params object?[] args) { }
        public void LogWarning(string message, params object?[] args) { }
        public void LogError(Exception? exception, string message, params object?[] args) { }
    }
}
