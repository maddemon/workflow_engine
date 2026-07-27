using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
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
[Obsolete("阶段五：NodeExecutionContextFactory 已降级为轻量适配壳。新代码应优先经管线阶段与独立 DI 服务（IHttpExecutionService / ICredentialService / ISubExecutionService 等）获取能力，而非经此工厂产出的上帝对象索取。", false)]
public sealed class NodeExecutionContextFactory(
    INodeRegistry registry,
    ScriptCache scriptCache,
    ParameterResolver parameterResolver,
    ICredentialAccessor credentialAccessor,
    IReadOnlySet<string> environmentWhitelist,
    ILogger<ParameterHydrator>? hydratorLogger = null,
    ILogger<JsEngine>? jsLogger = null,
    JsEngineOptions? jsEngineOptions = null,
    ILlmClient? llmClient = null,
    IWorkflowLoader? workflowLoader = null,
    IHttpClientPool? httpClientPool = null,
    IOAuth2TokenService? tokenService = null,
    ILlmClientFactory? llmClientFactory = null,
    Core.Abstractions.IShellExecutionGate? shellExecutionGate = null,
    IEventBus? eventBus = null) : Core.Abstractions.INodeExecutionContextFactory
{
    private readonly IOAuth2TokenService? _tokenService = tokenService;
    private readonly ILlmClientFactory? _llmClientFactory = llmClientFactory;
    private readonly IEventBus? _eventBus = eventBus;

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
        IReadOnlyDictionary<string, object?>? extraGlobals = null,
        IDictionary<string, object?>? nodeContext = null)
    {
        var nodeDefinition = node;
        var descriptor = registry.GetDescriptor(node.TypeName);
        var rawParameters = MergeParameters(nodeDefinition, descriptor);

        // CodeEditor/Script 的非 Script 字符串参数仍由节点自己执行，先抽出避免被 ParameterResolver 误求值
        var rawCodeParams = CodeParameterExtractor.Extract(rawParameters, descriptor);

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

        // 成功输出与最新批现以节点 Id 为键累积（见 ExecutionStage，避免同名节点互相覆盖），
        // 但下游表达式 $node['Name'] / $items('Name') 仍按节点名读取。
        // 此处经 workflow.Nodes 做 Id→Name 映射，重建按名索引的只读视图，保持表达式契约不变；
        // 同名的多个节点仅最后一个写入者生效（表达式语义本就无法区分同名节点）。
        var nodeNameById = workflow.Nodes.ToDictionary(n => n.Id, n => n.Name, StringComparer.OrdinalIgnoreCase);
        var nodeDict = new Dictionary<string, NodeOutput>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, batch) in successfulOutputs)
        {
            var nodeName = nodeNameById.TryGetValue(id, out var resolved) ? resolved : id;
            nodeDict[nodeName] = new NodeOutput(
                batch.Items.Select(i => (object?)i.Data).ToList());
        }

        // $items('Name') 读取的 latestBatches 同样需按名索引：经 Id→Name 映射重建只读视图。
        var latestBatchesByName = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, batch) in latestBatches)
        {
            var nodeName = nodeNameById.TryGetValue(id, out var resolved) ? resolved : id;
            latestBatchesByName[nodeName] = batch;
        }

        var credsAccessor = credentialAccessorOverride ?? credentialAccessor;
        if (_tokenService is not null)
        {
            credsAccessor = new OAuth2CredentialAccessor(credsAccessor, _tokenService);
        }

        // OBS-1：真实执行路径（未提供覆盖访问器，即使用注入的运行时访问器）下，
        // 用审计装饰器包裹凭据访问器，每次解析/解密凭据即发布 CredentialAccessedEvent。
        // Dry-run 等使用覆盖访问器的路径不审计，避免污染真实审计日志。
        if (_eventBus is not null && credentialAccessorOverride is null)
        {
            credsAccessor = new CredentialAuditAccessor(credsAccessor, _eventBus, execution.Id, node.Id);
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

        // 基础全局变量（供节点 body 经由 ExecutionScope.ApplyGlobalVariables 复用），
        // 须先于节点执行上下文构造，以便引擎复用与逐项作用域注入。
        var globalVariables = ExecutionContextGlobalsBuilder.BuildBase(
            credentialsDict, workflow, execution.Id, rawParameters, environmentWhitelist);
        if (nodeContext is not null)
        {
            globalVariables["$nodeContext"] = nodeContext;
        }

        // 提前构造节点执行上下文：EngineOptions/EngineLogger 已就绪，
        // 使 GetOrCreateEngine 创建/复用单次执行托管的单一引擎，避免每个 item 额外新建引擎。
        var nodeExecContext = new NodeExecutionContext
        {
            Workflow = workflow,
            ExecutionId = execution.Id,
            Node = nodeDefinition,
            RunIndex = runIndex,
            Inputs = inputs,
            RawParameters = rawParameters,
            Credentials = credsAccessor,
            Logger = NullExecutionLogger.Instance,
            CancellationToken = cancellationToken,
            LlmClient = llmClient,
            LlmClientFactory = _llmClientFactory,
            HttpClientPool = httpClientPool,
            NodeRegistry = registry,
            ContextFactory = this,
            WorkflowLoader = workflowLoader,
            ScriptCache = scriptCache,
            EngineOptions = jsEngineOptions,
            EngineLogger = jsLogger,
            GlobalVariables = globalVariables,
            NodeContext = nodeContext ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
        };

        // 复用同一引擎完成参数预求值：捕获引擎创建时的全局基线，求值结束后据此还原，
        // 既消除逐 item 的额外引擎创建/销毁开销，又避免预求值专用全局泄漏到节点执行体。
        var js = nodeExecContext.GetOrCreateEngine();
        var engineBaseline = new HashSet<string>(js.GetGlobalOwnKeys(), StringComparer.OrdinalIgnoreCase);

        // 统一全局变量表：既注入 JsEngine（供 ParameterResolver 与 Script 管线复用），
        // 也作为 ScriptContext.ExtraGlobals 传入，保证两种求值路径变量集一致。
        var globals = ExecutionContextGlobalsBuilder.BuildFull(
            credentialsDict, workflowDict, executionDict, ctxDict, nodeDict,
            currentInput, inputContainer, inputItems, latestBatchesByName, runIndex, environmentWhitelist);

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

        // 节点级持久化上下文 $nodeContext 注入 JS 引擎（plan-node-level-context）。
        // 提前到参数预求值之前注入，使参数脚本亦可引用 $nodeContext；body 表达式经
        // ExecutionScope.ApplyGlobalVariables 读取 context.GlobalVariables 的同源实例。
        if (nodeContext is not null)
        {
            js.SetValue("$nodeContext", nodeContext);
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
        await ScriptParameterPreEvaluator.PreEvaluateAsync(rawParameters, descriptor, scriptContext, js, scriptCache, cancellationToken)
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

        // SEC-1：依据 Shell 执行门禁（配置开关 + 当前用户角色）计算是否允许 RunInShell。
        // 门禁未注册时（如测试或精简宿主）一律视为禁止，遵循默认安全。
        var allowShellExecution = shellExecutionGate is not null
            && await shellExecutionGate.IsShellExecutionAllowedAsync(cancellationToken).ConfigureAwait(false);
        nodeExecContext.AllowShellExecution = allowShellExecution;

        // 还原引擎至基线：删除参数预求值期间注入的专用全局（extraGlobals/$items 等），
        // 节点执行体会经 ExecutionScope 自行注入逐项作用域，二者互不干扰。
        foreach (var key in js.GetGlobalOwnKeys())
        {
            if (!engineBaseline.Contains(key))
            {
                js.DeleteGlobal(key);
            }
        }

        nodeExecContext.ResolvedParameters = resolvedParameters;
        return nodeExecContext;
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

    private sealed class NullExecutionLogger : IExecutionLogger
    {
        public static readonly NullExecutionLogger Instance = new();
        public void LogInformation(string message, params object?[] args) { }
        public void LogWarning(string message, params object?[] args) { }
        public void LogError(Exception? exception, string message, params object?[] args) { }
    }
}
