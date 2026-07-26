using System.Linq;
using System.Threading;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Security;
using FlowEngine.Runtime.WaitingArea;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 纯内存工作流调度内核：驱动队列节点处理、输出路由、等待区与超时。
/// 不依赖 <c>DbContext</c> 与事件总线；持久化与事件发布经
/// <see cref="IExecutionSideEffects"/> 回调交由外壳处理，从而可被普通执行与 Dry-Run 复用。
/// <para>单一职责：本类仅负责调度循环编排与入口入队；具体节点执行（重试/超时）、输出路由、
/// 超时处理已下沉至 <see cref="NodeProcessor"/> / <see cref="OutputRouter"/> /
/// <see cref="RetryExecutor"/> / <see cref="TimeoutProcessor"/> 等协作者。</para>
/// </summary>
public sealed class WorkflowSchedulerKernel
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly EngineDefaultsOptions _defaults;
    private readonly OutputRouter _outputRouter;
    private readonly RetryExecutor _retryExecutor;
    private readonly NodeProcessor _nodeProcessor;
    private readonly TimeoutProcessor _timeoutProcessor;

    /// <summary>
    /// 构造纯内存工作流调度内核。
    /// </summary>
    /// <param name="nodeRegistry">节点注册中心。</param>
    /// <param name="contextFactory">节点执行上下文工厂。</param>
    /// <param name="errorHandler">错误策略处理。</param>
    /// <param name="secretMasker">敏感值脱敏器。</param>
    /// <param name="logger">日志。</param>
    /// <param name="defaultsOptions">引擎默认配置（可选）。</param>
    public WorkflowSchedulerKernel(
        INodeRegistry nodeRegistry,
        NodeExecutionContextFactory contextFactory,
        ErrorStrategyHandler errorHandler,
        SecretMasker secretMasker,
        ILogger<WorkflowSchedulerKernel> logger,
        IOptions<EngineDefaultsOptions>? defaultsOptions = null,
        IHttpExecutionService? httpExecutionService = null)
    {
        _nodeRegistry = nodeRegistry;
        _defaults = defaultsOptions?.Value ?? new EngineDefaultsOptions();

        // 协作者均在此一次性 new 出并复用，避免重复构造无状态实例。
        // 内核自身日志（ILogger<WorkflowSchedulerKernel>）直接传递给各协作者，
        // 协作者统一接受非泛型 ILogger，从而无需额外日志工厂即可继承内核的日志通道。
        _outputRouter = new OutputRouter(nodeRegistry, logger);
        _retryExecutor = new RetryExecutor(_defaults, errorHandler, logger);
        _nodeProcessor = new NodeProcessor(nodeRegistry, contextFactory, secretMasker, _retryExecutor, _outputRouter, _defaults, httpExecutionService);
        _timeoutProcessor = new TimeoutProcessor(nodeRegistry, errorHandler, secretMasker, _outputRouter);
    }

    /// <summary>
    /// 执行调度循环：驱动入口节点入队、逐节点处理、输出路由、等待区超时，直至完成或取消。
    /// </summary>
    /// <param name="session">执行会话（纯内存可变状态）。</param>
    /// <param name="sideEffects">副作用回调（持久化 / 事件发布）。</param>
    /// <param name="triggerPayload">触发负载。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RunAsync(
        ExecutionSession session,
        IExecutionSideEffects sideEffects,
        object? triggerPayload,
        CancellationToken cancellationToken)
    {
        session.StateMachine.Start();

        // OBS-2：发布工作流启动事件（闭合执行生命周期审计链）。
        await sideEffects.PublishWorkflowStartedAsync(
            session.Execution.Id, session.Execution.WorkflowDefinitionId, cancellationToken)
            .ConfigureAwait(false);

        await EnqueueEntryNodesAsync(session, triggerPayload, cancellationToken).ConfigureAwait(false);

        var cancelled = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _timeoutProcessor.ProcessAsync(session, sideEffects, cancellationToken).ConfigureAwait(false);

                if (session.Queue.Reader.TryRead(out var item))
                {
                    var shouldStop = await _nodeProcessor.ProcessAsync(item, session, sideEffects, cancellationToken).ConfigureAwait(false);

                    if (shouldStop)
                    {
                        session.StateMachine.Fail();
                        break;
                    }

                    continue;
                }

                if (session.WaitingArea.IsEmpty)
                {
                    break;
                }

                // CON-6：事件驱动唤醒，取代固定 500ms 空轮询。
                // 若队列已有可处理项（入队与脉冲之间存在竞态），直接进入下一轮 TryRead；
                // 否则阻塞于 SchedulerWake 信号（每次入队后脉冲），同时按等待区剩余超时自适应等待，
                // 保证超时节点仍能在不晚于该时长内被唤醒处理，消除无意义的忙等。
                if (session.Queue.Reader.TryPeek(out _))
                {
                    continue;
                }

                var minTimeout = session.WaitingArea.GetMinRemainingTimeoutDelay();
                try
                {
                    if (minTimeout == Timeout.InfiniteTimeSpan)
                    {
                        await session.SchedulerWake.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var wakeTask = session.SchedulerWake.WaitAsync(cancellationToken);
                        var delayTask = Task.Delay(minTimeout, cancellationToken);
                        await Task.WhenAny(wakeTask, delayTask).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 空闲等待期间被取消，退出循环交由下方统一处理 Cancelled。
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 节点处理 / 超时 / 副作用回调期间被取消：捕获后统一转为 Cancelled 终态，不向上抛。
            cancelled = true;
        }

        if (cancelled || cancellationToken.IsCancellationRequested)
        {
            session.StateMachine.Cancel();
            session.WaitingArea.CleanupExecution(session.Execution.Id);
        }
        else if (session.StateMachine.Status == ExecutionStatus.Running)
        {
            session.StateMachine.Complete();
        }

        session.Execution.Status = session.StateMachine.Status;
        if (session.StateMachine.Status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            session.Execution.CompletedAt = DateTime.UtcNow;
        }

        // 终态持久化与事件发布必须在取消后仍完成（保存 Cancelled/Completed 终态），
        // 故使用 CancellationToken.None：此时 cancellationToken 可能已取消，若传入会导致 SaveChangesAsync
        // 抛出而终态丢失。真实的取消传播已由逐节点的 PersistNodeRecordAsync 与失败态 PersistFailedStateAsync 承担。
        await sideEffects.PersistExecutionAsync(CancellationToken.None).ConfigureAwait(false);

        // Failed 终态：从节点执行记录中提取真实错误（最后一个失败节点的 Error），
        // 供 WorkflowFailedEvent 携带真实失败原因（errorTrigger 等消费者依赖此错误）。
        NodeError? failureError = null;
        if (session.StateMachine.Status == ExecutionStatus.Failed)
        {
            foreach (var record in session.Execution.NodeRecords)
            {
                if (!record.Output.Success && record.Output.Error is not null)
                {
                    failureError = record.Output.Error;
                }
            }
        }

        await sideEffects.PublishCompletedAsync(session.StateMachine.Status, CancellationToken.None, failureError).ConfigureAwait(false);
    }

    private async Task EnqueueEntryNodesAsync(
        ExecutionSession session,
        object? triggerPayload,
        CancellationToken cancellationToken)
    {
        var triggerBatch = SchedulerHelpers.CreateDataBatch(triggerPayload);
        var hasInputConnections = session.Workflow.Connections
            .Select(c => c.TargetNodeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var node in session.Workflow.Nodes)
        {
            var nodeType = _nodeRegistry.Get(node.TypeName);
            var isExplicitEntry = node.IsEntry || nodeType.DefaultIsEntry;
            var isImplicitEntry = !hasInputConnections.Contains(node.Id);

            // 零端口节点（既无输入也无输出端口，如纯注释 note 节点）无实际执行意义，
            // 直接跳过入队，避免徒增一条 NodeExecutionRecord。复用既有的端口名探测方法，不新增接口标志。
            var isAnnotation = OutputRouter.GetInputPortNames(nodeType).Count == 0 && OutputRouter.GetOutputPortNames(nodeType).Count == 0;
            if ((!isExplicitEntry && !isImplicitEntry) || isAnnotation)
            {
                continue;
            }

            var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
            var inputPorts = OutputRouter.GetInputPortNames(nodeType);
            if (inputPorts.Count > 0)
            {
                inputs[inputPorts[0]] = triggerBatch;
            }

            await EnqueueWorkAsync(session, new NodeWorkItem(session.Execution.Id, node.Id, inputs), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 入队节点工作项并脉冲调度唤醒信号（CON-6），使空闲的内核循环在入队瞬间被唤醒，
    /// 无需等待固定 500ms 轮询。
    /// </summary>
    private static async Task EnqueueWorkAsync(ExecutionSession session, NodeWorkItem item, CancellationToken cancellationToken)
    {
        await session.Queue.EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
        session.PulseScheduler();
    }
}
