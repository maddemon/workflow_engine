using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlowEngine.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Core.Abstractions;
/// <summary>节点基类，桥接 <see cref="INodeType"/>（框架契约）与 <see cref="INodeHandler"/>（业务契约）。
/// 通过反射读取 <see cref="NodeMetaAttribute"/> 与 <see cref="PortAttribute"/> 派生元数据；将 <see cref="NodeExecutionContext"/>
/// 转换为精简的 <see cref="NodeInput"/>，并把 <see cref="NodeHandlerOutput"/> 包装为 <see cref="NodeExecutionResult"/>。
/// 业务失败统一由 <see cref="NodeExecutionException"/> 表达，被捕获转换为失败结果（框架另行处理非业务异常）。</summary>
public abstract class NodeBase : INodeType, INodeHandler
{
    private readonly NodeMetaAttribute _meta;
    private readonly IReadOnlyList<PortDefinition> _ports;

    /// <summary>按 <see cref="Type"/> 缓存的节点元数据（[NodeMeta] 特性与端口定义），避免每次克隆实例都反射。
    /// 端口仅来自类型级 <see cref="PortAttribute"/>（运行期动态端口由 <see cref="GetExtraPorts"/> 在真实实例上另行派生）。</summary>
    private static readonly ConcurrentDictionary<Type, (NodeMetaAttribute Meta, PortDefinition[] Ports)> _metaCache = new();

    // ===== 框架受控访问（取代节点直接读取 NodeExecutionContext 服务定位器属性） =====
    // 命名均避开 FE0001 禁用标识符（精确匹配，见 NodeApiComplianceAnalyzer.ForbiddenNames）：
    // GetParameter/ErrorResult/HttpClientPool/NodeRegistry/ContextFactory/LlmClientFactory/ScriptCache/
    // ResolveCredentialAsync/ResolvedParameters/RawParameters。（IsAgentInvocation/AllowShellExecution/
    // NestingDepth/WorkflowLoader 经 [Inject] NodeExecutionContext Ctx 显式 opt-in，已不在禁用集。）
    // 节点自身参数统一经 typed property（由 ParameterHydrator 注入），不再缓存原始上下文；
    // 跨上下文读取（如 PaginateNode 从迭代子上下文读 url/bodyExpression/method）经 ReadResolvedParameter 窄 API 完成。

    /// <summary>从任意上下文读取已解析参数（供迭代子上下文使用，取代 iterContext.ResolvedParameters）。</summary>
    /// <param name="ctx">子执行上下文。</param>
    /// <param name="key">参数键。</param>
    /// <returns>参数值，或 null。</returns>
    protected static object? ReadResolvedParameter(NodeExecutionContext ctx, string key)
        => ctx.ResolvedParameters.TryGetValue(key, out var v) == true ? v : null;

    /// <summary>构造节点基类，从静态缓存按 <see cref="Type"/> 读取元数据（缺失则反射一次并缓存），消除每次克隆实例的反射开销。</summary>
    /// <exception cref="InvalidOperationException">当类型缺少 <see cref="NodeMetaAttribute"/> 或其 <see cref="NodeMetaAttribute.TypeName"/> 为空时抛出。</exception>
    protected NodeBase()
    {
        var (meta, ports) = _metaCache.GetOrAdd(GetType(), BuildMeta);
        _meta = meta;
        _ports = ports;
    }

    /// <summary>反射并校验指定类型的元数据，供 <see cref="_metaCache"/> 首次访问时调用（此后命中缓存）。</summary>
    /// <param name="t">节点类型。</param>
    /// <returns>类型级元数据与端口定义。</returns>
    private static (NodeMetaAttribute Meta, PortDefinition[] Ports) BuildMeta(Type t)
    {
        var meta = t.GetCustomAttribute<NodeMetaAttribute>()
                ?? throw new InvalidOperationException($"节点类型 {t.Name} 缺少 [NodeMeta] 特性。");
        if (string.IsNullOrEmpty(meta.TypeName))
        {
            throw new InvalidOperationException($"节点类型 {t.Name} 的 [NodeMeta] 特性缺少必填的 TypeName。");
        }

        return (meta, BuildPortsFromAttributes(t));
    }

    /// <summary>从 <see cref="PortAttribute"/> 构建类型级端口定义数组（不调用实例级 <see cref="GetExtraPorts"/>，因其依赖运行期状态、不可按类型缓存）。</summary>
    /// <param name="t">节点类型。</param>
    /// <returns>端口定义数组。</returns>
    private static PortDefinition[] BuildPortsFromAttributes(Type t)
    {
        var list = new List<PortDefinition>();
        foreach (var attr in t.GetCustomAttributes<PortAttribute>())
        {
            list.Add(new PortDefinition
            {
                Name = attr.Name,
                DisplayName = attr.DisplayName,
                Direction = attr.Direction,
                Type = attr.Type,
            });
        }

        return list.ToArray();
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
    ExecutionMode INodeType.ExecutionMode => _meta.ExecutionMode;

    /// <inheritdoc />
    IReadOnlyList<PortDefinition> INodeType.Ports => _ports;

    /// <inheritdoc />
    bool INodeType.DefaultIsEntry => _meta.DefaultIsEntry;

    /// <inheritdoc />
    async Task<NodeExecutionResult> INodeType.ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        // 写入受保护通道所需上下文（引擎等能力改由 NodeCapabilityInjector 经 [Inject] 注入，基类不释放）。

        // 兼容直接执行路径（如单元测试、非管线调用）：按节点声明的 [Inject] 能力补注入，
        // 等价于生产管线 ExecutionStage / SubExecutionService 的注入。上下文派生能力（Ctx / Logger /
        // LlmClient / NodeContext）始终取自 context；DI 能力仅当 context.NodeRegistry 非空时映射，
        // 其余 DI 能力（Http / Sub / Tools 等）若已由管线注入则保持原值，不被本次清空。
        InjectCapabilities(context);

        var input = new NodeInput(
            context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) ? batch : new DataBatch(),
            context.GlobalVariables,
            context.RunIndex,
            context.Inputs ?? new Dictionary<string, DataBatch>());

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

    /// <summary>
    /// 按节点声明的 <see cref="InjectAttribute"/> 注入能力，覆盖直接执行（测试/非管线）场景：
    /// 所有上下文派生的能力（含 <see cref="INodeRegistry"/>、<see cref="INodeExecutionContextFactory"/> 等）
    /// 均经 <see cref="NodeCapabilityInjector"/> 的 <c>ContextProviders</c> 从 <paramref name="context"/> 解析，
    /// DI 能力（如 Http / Sub / Tools）若已由生产管线注入则保持原值。不再为每次执行创建
    /// <see cref="ServiceCollection"/> + <see cref="IServiceProvider"/>，避免规模执行下的重复建容器开销。
    /// </summary>
    /// <param name="context">节点执行上下文。</param>
    private void InjectCapabilities(NodeExecutionContext context)
    {
        NodeCapabilityInjector.Inject(this, null, context);
    }

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
