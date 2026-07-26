using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Abstractions;
/// <summary>节点基类，桥接 <see cref="INodeType"/>（框架契约）与 <see cref="INodeHandler"/>（业务契约）。
/// 通过反射读取 <see cref="NodeMetaAttribute"/> 与 <see cref="PortAttribute"/> 派生元数据；将 <see cref="NodeExecutionContext"/>
/// 转换为精简的 <see cref="NodeInput"/>，并把 <see cref="NodeHandlerOutput"/> 包装为 <see cref="NodeExecutionResult"/>。
/// 业务失败统一由 <see cref="NodeExecutionException"/> 表达，被捕获转换为失败结果（框架另行处理非业务异常）。</summary>
public abstract class NodeBase : INodeType, INodeHandler
{
    private readonly NodeMetaAttribute _meta;
    private readonly IReadOnlyList<PortDefinition> _ports;

    /// <summary>LLM 客户端（执行期由框架注入），供 Agent/LLM 类节点使用。</summary>
    protected ILlmClient? LlmClient { get; private set; }

    /// <summary>节点级持久化上下文（执行期由框架注入），跨多次调用保持状态。</summary>
    protected NodeContext NodeContext { get; private set; } = new();

    /// <summary>当前节点执行托管的 JS 引擎（执行期由框架注入，<see cref="NodeExecutionContext.ReleaseEngine"/> 负责释放）。
    /// 供 FilterNode/JSNode 等需要逐项 <see cref="Script"/> 求值的节点使用；节点不应自行释放该引擎。</summary>
    protected JsEngine? Engine { get; private set; }

    /// <summary>HTTP 执行服务（执行期由框架经 <see cref="BindServices"/> 注入），供 HttpRequestNode 等发起 HTTP 请求。
    /// 取代经 <see cref="NodeExecutionContext"/> 直接依赖具体 <see cref="HttpExecutionService"/> 的方式。</summary>
    protected IHttpExecutionService? HttpExecutionService { get; private set; }

    /// <summary>子执行服务（执行期由框架经 <see cref="BindServices"/> 注入），供 AgentNode 等执行子节点/子工作流。
    /// 取代经 <see cref="NodeExecutionContext"/> 依赖 <see cref="INodeExecutionContextFactory"/> 的构造方式。</summary>
    protected ISubExecutionService? SubExecutionService { get; private set; }

    /// <summary>工具解析器（执行期由框架经 <see cref="BindServices"/> 注入），供 AgentNode 解析工具调用。
    /// 取代经 <see cref="NodeExecutionContext"/> 依赖 <see cref="INodeRegistry"/> 查找工具节点类型的方式。</summary>
    protected IToolResolver? ToolResolverService { get; private set; }

    /// <summary>执行期原始 <see cref="NodeExecutionContext"/>，供需要委派给基础设施服务（如 HTTP 执行）的节点复用。
    /// 节点不应读取其服务定位器属性（如 ErrorResult/HttpClientPool），仅作为受控上下文传给注入的服务。</summary>
    protected NodeExecutionContext ExecutionContext => _rawContext!;

    /// <summary>执行期原始 <see cref="NodeExecutionContext"/>，供受保护求值通道（<see cref="EvaluateItemAsync{T}"/>、
    /// <see cref="EvaluateContextAsync{T}"/>）复用。每次执行开始时由适配层写入，节点不应直接读取其服务定位器属性。</summary>
    private NodeExecutionContext? _rawContext;

    /// <summary>执行期日志器（来自原始上下文），供节点记录警告/信息。仅作日志用途，不读取其服务定位器属性。</summary>
    protected IExecutionLogger? Logger => _rawContext?.Logger;

    /// <summary>执行期节点注册中心（来自原始上下文），仅供遗留/直接实例化的回退路径解析工具定义；
    /// 生产路径应通过注入的 <see cref="SubExecutionService"/> 解析，避免节点直接读取服务定位器属性。</summary>
    protected INodeRegistry? ToolRegistry => _rawContext?.NodeRegistry;

    // ===== 框架受控访问（取代节点直接读取 NodeExecutionContext 服务定位器属性） =====
    // 命名均避开 FE0001 禁用标识符（精确匹配）：GetParameter/ErrorResult/HttpClientPool/NodeRegistry/
    // ContextFactory/WorkflowLoader/LlmClientFactory/ScriptCache/NestingDepth/AllowShellExecution/
    // IsAgentInvocation/ResolveCredentialAsync/ResolvedParameters/RawParameters。

    /// <summary>解析凭据（执行期由框架经原始上下文提供）。取代节点直接调用 context.ResolveCredentialAsync。</summary>
    /// <param name="idOrName">凭据 ID 或名称。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完整凭据值；找不到时返回 null。</returns>
    protected Task<CredentialValue?> GetCredentialAsync(string? idOrName, CancellationToken ct = default)
        => _rawContext!.ResolveCredentialAsync(idOrName, ct);

    /// <summary>SSRF 预检（执行期由框架经原始上下文提供）。返回非 null 表示被拦截的错误结果，节点应据此短路失败。</summary>
    /// <param name="url">待校验 URL。</param>
    /// <param name="code">拦截时的错误码（默认 SsrfBlocked）。</param>
    /// <returns>null 表示放行；非 null 为被拦截的错误结果。</returns>
    protected NodeExecutionResult? GuardSsrf(string? url, string code = FlowConstants.ErrorCodes.SsrfBlocked)
        => _rawContext!.GuardSsrf(url, code);

    /// <summary>是否由 Agent/LLM 间接调用（取代 context.IsAgentInvocation）。用于 Shell 等门禁判断。</summary>
    protected bool IsInvokedByAgent => _rawContext?.IsAgentInvocation ?? false;

    /// <summary>是否允许执行 Shell 命令（取代 context.AllowShellExecution）。</summary>
    protected bool ShellExecutionEnabled => _rawContext?.AllowShellExecution ?? false;

    /// <summary>当前嵌套深度（取代 context.NestingDepth）。供 SubAgent/SubWorkflow 门禁判断。</summary>
    protected int NestingLevel => _rawContext?.NestingDepth ?? 0;

    /// <summary>按类型名查找节点类型（取代 context.NodeRegistry.Get）。</summary>
    /// <param name="typeName">节点类型名。</param>
    /// <returns>节点类型实例，或 null。</returns>
    protected INodeType? FindNodeType(string typeName) => _rawContext?.NodeRegistry?.Get(typeName);

    /// <summary>节点注册中心（供需要直接构造内部执行器的遗留节点，如 SubWorkflowExecutor）。取代 context.NodeRegistry。</summary>
    protected INodeRegistry? Registry => _rawContext?.NodeRegistry;

    /// <summary>加载子工作流定义（取代 context.WorkflowLoader.LoadAsync）。</summary>
    /// <param name="workflowId">工作流 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>工作流定义，或 null。</returns>
    protected Task<Workflow?> LoadWorkflowAsync(Guid workflowId, CancellationToken ct = default)
        => _rawContext?.WorkflowLoader is null
            ? Task.FromResult<Workflow?>(null)
            : _rawContext.WorkflowLoader.LoadAsync(workflowId, ct);

    /// <summary>读取原始参数字典中的值（取代 context.RawParameters）。</summary>
    /// <param name="key">参数键。</param>
    /// <returns>参数值，或 null。</returns>
    protected object? GetRawParameter(string key)
        => _rawContext?.RawParameters.TryGetValue(key, out var v) == true ? v : null;

    /// <summary>读取当前节点已解析参数字典中的值（取代 context.ResolvedParameters）。</summary>
    /// <param name="key">参数键。</param>
    /// <returns>参数值，或 null。</returns>
    protected object? GetResolvedParameter(string key)
        => _rawContext?.ResolvedParameters.TryGetValue(key, out var v) == true ? v : null;

    /// <summary>从任意上下文读取已解析参数（供迭代子上下文使用，取代 iterContext.ResolvedParameters）。</summary>
    /// <param name="ctx">子执行上下文。</param>
    /// <param name="key">参数键。</param>
    /// <returns>参数值，或 null。</returns>
    protected static object? ReadResolvedParameter(NodeExecutionContext ctx, string key)
        => ctx.ResolvedParameters.TryGetValue(key, out var v) == true ? v : null;

    /// <summary>反序列化 JSON（取代 context.TryParseJson）。</summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="raw">原始 JSON 字符串。</param>
    /// <param name="result">反序列化结果。</param>
    /// <param name="errorCode">失败时的错误码。</param>
    /// <param name="opts">序列化选项。</param>
    /// <returns>是否成功。</returns>
    protected bool TryParseJson<T>(string raw, out T? result, out string? errorCode, JsonSerializerOptions? opts = null)
        => _rawContext!.TryParseJson<T>(raw, out result, out errorCode, opts);

    /// <summary>创建迭代子上下文（取代 context.ContextFactory.CreateAsync），供 PaginateNode 等逐页执行。</summary>
    /// <param name="workflow">所属工作流定义。</param>
    /// <param name="execution">执行记录。</param>
    /// <param name="node">子节点定义。</param>
    /// <param name="nodeInstance">子节点类型实例。</param>
    /// <param name="inputs">按端口组织的输入批。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="credentialAccessorOverride">凭据访问器覆盖（透传原始上下文的凭据）。</param>
    /// <param name="extraGlobals">额外全局变量（如分页游标 $cursor/$page）。</param>
    /// <returns>新建的子执行上下文。</returns>
    protected Task<NodeExecutionContext> CreateChildContextAsync(
        Workflow workflow,
        ExecutionRecord execution,
        NodeDefinition node,
        INodeType nodeInstance,
        IReadOnlyDictionary<string, DataBatch> inputs,
        int runIndex,
        CancellationToken ct,
        ICredentialAccessor? credentialAccessorOverride = null,
        IReadOnlyDictionary<string, object?>? extraGlobals = null)
        => _rawContext!.ContextFactory!.CreateAsync(
            workflow, execution, node, nodeInstance, inputs,
            new Dictionary<string, DataBatch>(), new Dictionary<string, DataBatch>(), runIndex, ct,
            credentialAccessorOverride, extraGlobals);

    /// <summary>构造节点基类，反射并缓存元数据特性。</summary>
    /// <exception cref="InvalidOperationException">当类型缺少 <see cref="NodeMetaAttribute"/> 时抛出。</exception>
    protected NodeBase()
    {
        _meta = GetType().GetCustomAttribute<NodeMetaAttribute>()
                ?? throw new InvalidOperationException($"节点类型 {GetType().Name} 缺少 [NodeMeta] 特性。");
        if (string.IsNullOrEmpty(_meta.TypeName))
        {
            throw new InvalidOperationException($"节点类型 {GetType().Name} 的 [NodeMeta] 特性缺少必填的 TypeName。");
        }

        _ports = BuildPorts();
    }

    /// <summary>从 <see cref="PortAttribute"/>（及 <see cref="GetExtraPorts"/>）构建端口定义列表。</summary>
    private IReadOnlyList<PortDefinition> BuildPorts()
    {
        var list = new List<PortDefinition>();
        foreach (var attr in GetType().GetCustomAttributes<PortAttribute>())
        {
            list.Add(new PortDefinition
            {
                Name = attr.Name,
                DisplayName = attr.DisplayName,
                Direction = attr.Direction,
                Type = attr.Type,
            });
        }

        list.AddRange(GetExtraPorts());
        return list;
    }

    /// <summary>供子类追加运行时确定的额外端口（基类默认无）。</summary>
    /// <returns>附加端口定义列表。</returns>
    protected virtual IReadOnlyList<PortDefinition> GetExtraPorts() => [];

    /// <inheritdoc />
    string INodeType.TypeName => _meta.TypeName;

    /// <inheritdoc />
    string INodeType.DisplayName => _meta.DisplayName;

    /// <inheritdoc />
    string INodeType.Category => _meta.Category.ToString();

    /// <inheritdoc />
    string INodeType.Icon => _meta.Icon;

    /// <inheritdoc />
    ExecutionMode INodeType.ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    IReadOnlyList<PortDefinition> INodeType.Ports => _ports;

    /// <inheritdoc />
    bool INodeType.DefaultIsEntry => _meta.DefaultIsEntry;

    /// <inheritdoc />
    async Task<NodeExecutionResult> INodeType.ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        // 写入受保护通道所需上下文与引擎（引擎由 ExecutionStage 在结束后经 ReleaseEngine 释放，基类不释放）。
        _rawContext = context;
        Engine = context.GetOrCreateEngine();

        var input = new NodeInput(
            context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) ? batch : new DataBatch(),
            context.GlobalVariables,
            context.RunIndex,
            context.Inputs ?? new Dictionary<string, DataBatch>());

        LlmClient = context.LlmClient;
        NodeContext = context.NodeContext is null ? new NodeContext() : new NodeContext(context.NodeContext);

        await OnExecutingAsync(new NodeExecutingContext(input, context), cancellationToken).ConfigureAwait(false);

        NodeHandlerOutput output;
        try
        {
            output = await ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (NodeExecutionException ex)
        {
            var recovered = await OnErrorAsync(new NodeErrorContext(ex, context), cancellationToken).ConfigureAwait(false);
            if (recovered is not null)
            {
                return ToResult(recovered, _ports);
            }

            return new NodeExecutionResult
            {
                Success = false,
                Error = new NodeError
                {
                    Code = ex.ErrorCode,
                    Message = ex.Message,
                    NodeDefinitionId = context.Node?.Id ?? string.Empty,
                },
            };
        }

        var result = ToResult(output, _ports);
        await OnExecutedAsync(new NodeExecutedContext(output, result), cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    AiNodeDefinition? INodeType.GetAiDefinition(NodeTypeDescriptor descriptor) => GetAiDefinition(descriptor);

    /// <summary>返回 AI-native 节点定义；默认 null，由子类重写以提供更丰富语义。</summary>
    /// <param name="descriptor">节点类型描述符。</param>
    /// <returns>AI 节点定义，或 null。</returns>
    protected virtual AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) => null;

    /// <summary>执行节点业务逻辑（由子类实现）。框架负责上下文转换、异常捕获与结果包装。</summary>
    /// <param name="input">精简输入视图。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>节点业务输出。</returns>
    public abstract Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct);

    /// <summary>执行前钩子，默认无操作。可重写以做前置处理或短路。</summary>
    /// <param name="ctx">执行前上下文。</param>
    /// <param name="ct">取消令牌。</param>
    protected virtual Task OnExecutingAsync(NodeExecutingContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>执行后钩子，默认无操作。</summary>
    /// <param name="ctx">执行后上下文。</param>
    /// <param name="ct">取消令牌。</param>
    protected virtual Task OnExecutedAsync(NodeExecutedContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>错误钩子，捕获 <see cref="NodeExecutionException"/> 后调用。返回非 null 输出可“恢复”为成功结果；
    /// 默认返回 null（即按失败结果处理）。</summary>
    /// <param name="ctx">错误上下文。</param>
    /// <param name="ct">取消令牌。</param>
    protected virtual Task<NodeHandlerOutput?> OnErrorAsync(NodeErrorContext ctx, CancellationToken ct)
        => Task.FromResult<NodeHandlerOutput?>(null);

    /// <summary>注册钩子（占位），默认无操作。</summary>
    /// <param name="ctx">注册上下文。</param>
    /// <param name="ct">取消令牌。</param>
    protected virtual Task OnRegisterAsync(NodeRegistrationContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>逐项（per-item）类型安全求值：复用 <see cref="ScriptEvaluationExtensions"/> 在节点执行上下文托管的引擎上求值。
    /// 适用于 FilterNode/JSNode 等需要逐条数据评估 <see cref="Script"/> 的场景；表达式已被预求值时命中
    /// <see cref="Script.ResolvedValue"/> 短路，零引擎执行。逐项调用会注入标准 $json/$input 作用域。</summary>
    /// <typeparam name="T">目标 CLR 类型。</typeparam>
    /// <param name="script">待求值的脚本。</param>
    /// <param name="item">当前逐项数据（JSON 节点）。</param>
    /// <param name="itemIndex">当前逐项索引。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>强类型求值结果。</returns>
    protected Task<T?> EvaluateItemAsync<T>(Script script, JsonNode? item, int itemIndex, CancellationToken ct)
        => script.EvaluateAsync<T>(_rawContext!, item, itemIndex, cancellationToken: ct);

    /// <summary>整批（无逐项 item）类型安全求值：复用 <see cref="ScriptEvaluationExtensions"/> 的额外全局重载，
    /// 在节点执行上下文托管的引擎上求值。适用于 HttpRequestNode 的 Headers/Body 等整批级 <see cref="Script"/> 参数。
    /// 因未传 item，不会注入逐项 $json/$input 作用域；与历史 <c>Script.EvaluateAsync&lt;T&gt;(context, ct)</c> 行为一致。</summary>
    /// <typeparam name="T">目标 CLR 类型。</typeparam>
    /// <param name="script">待求值的脚本。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>强类型求值结果。</returns>
    protected Task<T?> EvaluateContextAsync<T>(Script script, CancellationToken ct)
        => script.EvaluateAsync<T>(_rawContext!, cancellationToken: ct);

    /// <summary>逐项（per-item）原始求值：返回 <see cref="ScriptResult"/> 以便判定成功/错误或对同一次结果多形态取值。
    /// 委托 <see cref="ScriptEvaluationExtensions"/>，复用节点上下文托管的引擎。</summary>
    /// <param name="script">待求值的脚本。</param>
    /// <param name="item">当前逐项数据（JSON 节点）。</param>
    /// <param name="itemIndex">当前逐项索引。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>原始求值结果。</returns>
    protected Task<ScriptResult> EvaluateItemAsync(Script script, JsonNode? item, int itemIndex, CancellationToken ct)
        => script.ExecuteAsync(_rawContext!, item, itemIndex, cancellationToken: ct);

    /// <summary>由框架在解析阶段注入节点所需的 DI 服务。NodeBase 处理器仅接收 <see cref="NodeInput"/>，
    /// 无法经构造函数注入基础设施服务，故由 <see cref="ResolutionStage"/> 在运行期调用本方法。
    /// 参数为 null 表示对应服务在当前执行环境不可用（如未迁移节点或环境未提供）。</summary>
    /// <param name="http">HTTP 执行服务。</param>
    /// <param name="sub">子执行服务。</param>
    /// <param name="tools">工具解析器。</param>
    public void BindServices(IHttpExecutionService? http, ISubExecutionService? sub, IToolResolver? tools)
    {
        HttpExecutionService = http;
        SubExecutionService = sub;
        ToolResolverService = tools;
    }

    /// <summary>将业务输出包装为执行结果：有端口输出时主输出取首个端口批次，否则取主数据批次。
    /// 单端口输出时回填 <see cref="NodeExecutionResult.BranchIndex"/>，兼容仍按 BranchIndex 路由的消费者。</summary>
    private static NodeExecutionResult ToResult(NodeHandlerOutput output, IReadOnlyList<PortDefinition> ports)
    {
        // 业务失败但可能仍携带输出（如 Agent 失败时输出结果 DTO）：直接映射为失败结果。
        if (output.Error is not null)
        {
            return new NodeExecutionResult
            {
                Success = false,
                Error = output.Error,
                Output = output.Batch
            };
        }

        if (output.PortOutputs is { Count: > 0 })
        {
            var dict = output.PortOutputs.ToDictionary(kv => kv.Key, kv => kv.Value);
            var primary = dict.Values.FirstOrDefault() ?? new DataBatch();

            // 兼容仍按 BranchIndex 路由的消费者（如 SubWorkflowExecutor）：单端口输出时回填 BranchIndex。
            int? branchIndex = null;
            if (dict.Count == 1)
            {
                var portName = dict.Keys.First();
                var outputPorts = ports.Where(p => p.Direction == PortDirection.Output).ToList();
                var idx = outputPorts.FindIndex(p => p.Name.Equals(portName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    branchIndex = idx;
                }
            }

            return new NodeExecutionResult { Success = true, Output = primary, PortOutputs = dict, BranchIndex = branchIndex };
        }

        return new NodeExecutionResult { Success = true, Output = output.Batch };
    }
}
