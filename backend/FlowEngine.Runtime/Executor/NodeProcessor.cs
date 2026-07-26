using System.Collections.Concurrent;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Execution.Pipeline;
using FlowEngine.Runtime.Execution.Stages;
using FlowEngine.Runtime.Security;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 单个节点的处理：从 <see cref="WorkflowSchedulerKernel"/> 抽离的单一职责协作者。
/// 现仅作编排：解析节点、构造 <see cref="NodePipelineContext"/> 与七个 <see cref="IExecutionStage"/>，
/// 交由 <see cref="NodePipeline"/> 驱动执行，并将管线结果翻译回调度器的 shouldStop 语义
/// （环路上限 / 节点失败且错误策略非 Continue → true；取消 → false）。
/// </summary>
public sealed class NodeProcessor
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;
    private readonly SecretMasker _secretMasker;
    private readonly RetryExecutor _retryExecutor;
    private readonly OutputRouter _outputRouter;
    private readonly EngineDefaultsOptions _defaults;
    private readonly IHttpExecutionService? _httpExecutionService;
    private readonly ISubExecutionService? _subExecutionService;
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    /// 构造节点处理器。
    /// </summary>
    /// <param name="nodeRegistry">节点注册中心。</param>
    /// <param name="contextFactory">节点执行上下文工厂。</param>
    /// <param name="secretMasker">敏感值脱敏器。</param>
    /// <param name="retryExecutor">重试执行器。</param>
    /// <param name="outputRouter">输出路由器。</param>
    /// <param name="defaults">引擎默认配置（环路上限、保留输出上限等）。</param>
    /// <param name="httpExecutionService">HTTP 执行服务（可选；供迁移后的 HttpRequestNode 注入）。</param>
    /// <param name="subExecutionService">子执行服务（可选；供迁移后的 AgentNode 注入，scoped 服务由调用方决定注入方式）。</param>
    public NodeProcessor(
        INodeRegistry nodeRegistry,
        NodeExecutionContextFactory contextFactory,
        SecretMasker secretMasker,
        RetryExecutor retryExecutor,
        OutputRouter outputRouter,
        EngineDefaultsOptions defaults,
        IHttpExecutionService? httpExecutionService = null,
        ISubExecutionService? subExecutionService = null,
        IServiceProvider serviceProvider = null!)
    {
        _nodeRegistry = nodeRegistry;
        _contextFactory = contextFactory;
        _secretMasker = secretMasker;
        _retryExecutor = retryExecutor;
        _outputRouter = outputRouter;
        _defaults = defaults;
        _httpExecutionService = httpExecutionService;
        _subExecutionService = subExecutionService;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 处理单个节点工作项：构造管线上下文与七个阶段，交由 <see cref="NodePipeline"/> 驱动执行，
    /// 并将结果翻译回"是否应终止调度"。
    /// </summary>
    /// <param name="item">待处理工作项。</param>
    /// <param name="session">执行会话。</param>
    /// <param name="sideEffects">副作用回调（持久化 / 事件发布）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否应终止调度（节点失败且错误策略非 Continue，或环路上限触发）。</returns>
    public async Task<bool> ProcessAsync(
        NodeWorkItem item,
        ExecutionSession session,
        IExecutionSideEffects sideEffects,
        CancellationToken cancellationToken)
    {
        var context = new NodePipelineContext(item, session, sideEffects);
        var pipeline = new NodePipeline(new IExecutionStage[]
        {
            new InitializeStage(_nodeRegistry, _defaults),
            new ValidationStage(_nodeRegistry),
            new ResolutionStage(),
            new ExecutionStage(_contextFactory, _retryExecutor, _secretMasker, _defaults, _serviceProvider),
            new PostProcessStage(),
            new RoutingStage(_outputRouter),
            new PersistenceStage(_secretMasker),
        });

        await pipeline.RunAsync(context, cancellationToken).ConfigureAwait(false);

        // shouldStop 语义：短路（环路上限 / 节点失败且非 Continue）→ true；取消 → false。
        if (context.ShouldTerminateWorkflow)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 由节点执行上下文构造节点执行记录，并对输入/输出/参数做敏感值脱敏。
    /// 透传至 <see cref="NodeExecutionHelpers.BuildNodeExecutionRecord"/>（供遗留反射测试复用单一实现）。
    /// </summary>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <param name="inputs">本次运行输入。</param>
    /// <param name="output">本次运行输出。</param>
    /// <param name="context">节点执行上下文（含脱敏所需的原始/已解析参数与记录 ID）。</param>
    /// <param name="sensitiveValues">敏感值集合（字面凭据）。</param>
    /// <param name="startedAt">节点执行开始时间。</param>
    /// <returns>脱敏后的节点执行记录。</returns>
    private NodeExecutionRecord BuildNodeExecutionRecord(
        string nodeDefinitionId,
        int runIndex,
        IReadOnlyDictionary<string, DataBatch> inputs,
        NodeExecutionResult output,
        NodeExecutionContext context,
        IReadOnlySet<string> sensitiveValues,
        DateTime startedAt)
        => NodeExecutionHelpers.BuildNodeExecutionRecord(
            nodeDefinitionId, runIndex, inputs, output, context, sensitiveValues, startedAt, _secretMasker);

    /// <summary>
    /// 限制单节点保留输出项数（CON-5）。透传至 <see cref="NodeExecutionHelpers.CapRetainedOutput"/>。
    /// </summary>
    /// <param name="session">执行会话。</param>
    /// <param name="nodeName">节点名。</param>
    private void CapRetainedOutput(ExecutionSession session, string nodeName)
        => NodeExecutionHelpers.CapRetainedOutput(session, nodeName, _defaults.MaxRetainedOutputItems);
}
