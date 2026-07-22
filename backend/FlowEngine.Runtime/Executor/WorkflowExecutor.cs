using System.Linq;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Executor;

public sealed class WorkflowExecutor : IEngine
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly INodeRegistry _nodeRegistry;
    private readonly WorkflowExecutionQueue _executionQueue;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<WorkflowExecutor> _logger;
    private readonly WorkflowSchedulerKernel _kernel;

    public WorkflowExecutor(
        FlowEngineDbContext dbContext,
        INodeRegistry nodeRegistry,
        NodeExecutionContextFactory contextFactory,
        ErrorStrategyHandler errorHandler,
        WorkflowExecutionQueue executionQueue,
        ILogger<WorkflowExecutor> logger,
        ILogger<WorkflowSchedulerKernel> kernelLogger,
        SecretMasker secretMasker,
        IEventBus? eventBus = null,
        IOptions<EngineDefaultsOptions>? defaultsOptions = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _nodeRegistry = nodeRegistry ?? throw new ArgumentNullException(nameof(nodeRegistry));
        _executionQueue = executionQueue ?? throw new ArgumentNullException(nameof(executionQueue));
        _eventBus = eventBus;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _kernel = new WorkflowSchedulerKernel(
            nodeRegistry ?? throw new ArgumentNullException(nameof(nodeRegistry)),
            contextFactory ?? throw new ArgumentNullException(nameof(contextFactory)),
            errorHandler ?? throw new ArgumentNullException(nameof(errorHandler)),
            secretMasker ?? throw new ArgumentNullException(nameof(secretMasker)),
            kernelLogger ?? throw new ArgumentNullException(nameof(kernelLogger)),
            defaultsOptions);
    }

    /// <summary>
    /// 启动指定工作流。由引擎内部加载工作流定义。
    /// </summary>
    /// <param name="workflowDefinitionId">工作流定义 ID。</param>
    /// <param name="triggerPayload">触发负载。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行 ID。</returns>
    public async Task<ExecutionId> StartAsync(
        Guid workflowDefinitionId,
        object? triggerPayload = null,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _dbContext.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowDefinitionId, cancellationToken)
            .ConfigureAwait(false);

        if (workflow is null)
        {
            throw new NotFoundException($"工作流 '{workflowDefinitionId}' 不存在。");
        }

        return await EnqueueExecutionAsync(workflow, workflowDefinitionId, triggerPayload, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 启动指定工作流（复用调用方已加载的工作流定义，避免重复查询）。
    /// </summary>
    /// <param name="workflowDefinitionId">工作流定义 ID。</param>
    /// <param name="preloadedWorkflow">调用方已加载的工作流定义；其 Id 须等于 workflowDefinitionId，否则改回内部加载。</param>
    /// <param name="triggerPayload">触发负载。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行 ID。</returns>
    public async Task<ExecutionId> StartAsync(
        Guid workflowDefinitionId,
        Workflow preloadedWorkflow,
        object? triggerPayload = null,
        CancellationToken cancellationToken = default)
    {
        // 复用调用方已加载的工作流；Id 不匹配时回退内部加载，避免误用。
        var workflow = (preloadedWorkflow is not null && preloadedWorkflow.Id == workflowDefinitionId)
            ? preloadedWorkflow
            : await _dbContext.Workflows.AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == workflowDefinitionId, cancellationToken)
                .ConfigureAwait(false);

        if (workflow is null)
        {
            throw new NotFoundException($"工作流 '{workflowDefinitionId}' 不存在。");
        }

        return await EnqueueExecutionAsync(workflow, workflowDefinitionId, triggerPayload, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 创建执行记录并加入执行队列，返回执行 ID。两种 <see cref="StartAsync"/> 重载共用的核心逻辑。
    /// </summary>
    /// <param name="workflow">已加载的工作流定义。</param>
    /// <param name="workflowDefinitionId">工作流定义 ID。</param>
    /// <param name="triggerPayload">触发负载。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行 ID。</returns>
    private async Task<ExecutionId> EnqueueExecutionAsync(
        Workflow workflow,
        Guid workflowDefinitionId,
        object? triggerPayload,
        CancellationToken cancellationToken)
    {
        var executionRecord = new ExecutionRecord
        {
            WorkflowDefinitionId = workflowDefinitionId,
            ProjectId = workflow.ProjectId, // 冗余存储，便于直接按项目隔离查询（GAP-11）
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };

        _dbContext.ExecutionRecords.Add(executionRecord);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _executionQueue.EnqueueAsync(
            new WorkflowExecutionWorkItem(executionRecord.Id, workflowDefinitionId, triggerPayload, workflow),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("执行 {ExecutionId} 已加入队列。", executionRecord.Id);

        return ExecutionId.From(executionRecord.Id);
    }

    public async Task ExecuteLoopAsync(
        Workflow workflow,
        Guid executionRecordId,
        object? triggerPayload,
        FlowEngineDbContext executionStore,
        CancellationToken cancellationToken)
    {
        var execution = await executionStore.ExecutionRecords
            .FirstOrDefaultAsync(e => e.Id == executionRecordId, cancellationToken)
            .ConfigureAwait(false);
        if (execution is null) return;

        // 已被取消（如未出队时由 CancelAsync 直接落库 Cancelled）或已进入其他终态：不再执行，避免覆写终态。
        if (execution.Status is ExecutionStatus.Cancelled or ExecutionStatus.Completed or ExecutionStatus.Failed
            or ExecutionStatus.Compensated or ExecutionStatus.CompensationFailed or ExecutionStatus.DryRunCompleted)
        {
            return;
        }

        var session = new ExecutionSession(workflow, execution, executionRecordId, _nodeRegistry)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };
        // 状态机由 WorkflowSchedulerKernel.RunAsync 负责启动；此处仅将待处理的 Pending 执行标记为 Running 并落库。
        // 仅当仍为 Pending 时才覆写为 Running，避免覆盖 CancelAsync 已写入的 Cancelled 终态。
        if (execution.Status == ExecutionStatus.Pending)
        {
            session.Execution.Status = ExecutionStatus.Running;
            await executionStore.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var sideEffects = new ExecutorSideEffects(executionStore, execution, _eventBus, _logger);
        await _kernel.RunAsync(session, sideEffects, triggerPayload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 普通执行的副作用实现：将内核的纯内存调度结果落库并发布事件。
    /// </summary>
    private sealed class ExecutorSideEffects : IExecutionSideEffects
    {
        private readonly FlowEngineDbContext _store;
        private readonly ExecutionRecord _execution;
        private readonly IEventBus? _eventBus;
        private readonly ILogger<WorkflowExecutor> _logger;

        // 节点记录已加入内存中的 Execution.NodeRecords。为避免每节点整体重写 JSON 列造成的写放大，
        // 仅每累计 NodeFlushThreshold 条才真正落库一次，将 SaveChangesAsync 调用从 O(N) 降为约 O(N/25)+1。
        // 终态 PersistExecutionAsync 与失败态 PersistFailedStateAsync 会兜底刷新尾部记录，保证不丢数据。
        private int _pendingNodeWrites;

        private const int NodeFlushThreshold = 25;

        public ExecutorSideEffects(FlowEngineDbContext store, ExecutionRecord execution, IEventBus? eventBus, ILogger<WorkflowExecutor> logger)
        {
            _store = store;
            _execution = execution;
            _eventBus = eventBus;
            _logger = logger;
        }

        public async Task PersistNodeRecordAsync(NodeExecutionRecord record, CancellationToken cancellationToken)
        {
            _pendingNodeWrites++;
            if (_pendingNodeWrites % NodeFlushThreshold != 0)
            {
                // 阈值内不落库：记录已在内存的 Execution.NodeRecords 中，交由周期性刷新或终态刷新持久化。
                return;
            }

            _store.Entry(_execution).Property(e => e.NodeRecords).IsModified = true;
            try
            {
                await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("节点 {NodeId} 执行记录批量保存被取消。", record.NodeDefinitionId);
            }
        }

        public Task PersistFailedStateAsync(CancellationToken cancellationToken)
        {
            // 失败立即落库：标记节点记录为已修改并保存，确保已收集的记录（含失败节点本身）全部持久化。
            _store.Entry(_execution).Property(e => e.NodeRecords).IsModified = true;
            return _store.SaveChangesAsync(cancellationToken);
        }

        public Task PersistExecutionAsync(CancellationToken cancellationToken)
        {
            // 终态落库：刷新阈值边界之外尚未刷盘的尾部记录，保证无数据丢失。
            _store.Entry(_execution).Property(e => e.NodeRecords).IsModified = true;
            return _store.SaveChangesAsync(cancellationToken);
        }

        public async Task PublishNodeStartedAsync(Guid executionId, string nodeId, int runIndex, CancellationToken cancellationToken)
        {
            if (_eventBus is null) return;
            await _eventBus.PublishAsync(new NodeStartedEvent(executionId, nodeId, runIndex), cancellationToken).ConfigureAwait(false);
        }

        public async Task PublishCompletedAsync(ExecutionStatus status, CancellationToken cancellationToken, NodeError? error = null)
        {
            if (_eventBus is null) return;

            AuditEvent? completedEvent = status switch
            {
                ExecutionStatus.Completed => new WorkflowCompletedEvent(
                    _execution.Id, _execution.WorkflowDefinitionId, ExecutionStatus.Completed),
                ExecutionStatus.Failed => new WorkflowFailedEvent(
                    _execution.Id, _execution.WorkflowDefinitionId, error),
                ExecutionStatus.Cancelled => new WorkflowCancelledEvent(
                    _execution.Id, _execution.WorkflowDefinitionId),
                _ => null
            };
            if (completedEvent is not null)
            {
                await _eventBus.PublishAsync(completedEvent, cancellationToken).ConfigureAwait(false);
            }
        }

        public Func<LlmStreamChunk, CancellationToken, Task> CreateLlmStreamCallback(Guid executionId, string nodeId, int runIndex)
        {
            if (_eventBus is null) return (_, _) => Task.CompletedTask;

            return async (chunk, ct) =>
            {
                try
                {
                    await _eventBus.PublishAsync(new LlmTokenStreamEvent
                    {
                        ExecutionId = executionId,
                        NodeDefinitionId = nodeId,
                        RunIndex = runIndex,
                        Delta = chunk.Delta,
                        IsFinal = chunk.IsFinal,
                    }, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to publish LlmTokenStreamEvent for node {NodeDefinitionId}.",
                        nodeId);
                }
            };
        }
    }
}
