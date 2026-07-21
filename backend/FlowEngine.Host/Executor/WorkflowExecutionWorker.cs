using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Host.Executor;

/// <summary>
/// 工作流执行后台服务，从 <see cref="WorkflowExecutionQueue"/> 读取工作项并驱动 <see cref="WorkflowExecutor"/> 执行。
/// 使用 <see cref="IHostApplicationLifetime.ApplicationStopping"/> 作为取消令牌，确保应用关闭时优雅终止；
/// 每次执行额外登记按 executionId 索引的 <see cref="CancellationTokenSource"/>，供 <see cref="ExecutionService.CancelAsync"/> 取消。
/// </summary>
public sealed class WorkflowExecutionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<WorkflowExecutionWorker> _logger;

    public WorkflowExecutionWorker(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        ILogger<WorkflowExecutionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("工作流执行后台服务已启动。");

        using var scope = _scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<WorkflowExecutionQueue>();
        var cancellationRegistry = scope.ServiceProvider.GetRequiredService<ExecutionCancellationRegistry>();

        while (!stoppingToken.IsCancellationRequested)
        {
            WorkflowExecutionWorkItem item;
            try
            {
                item = await queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("收到执行工作项 {ExecutionId}。", item.ExecutionRecordId);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                // P3 #20：每个执行项在独立 scope 内解析 WorkflowExecutor（其 scoped DbContext 随 scope 释放），
                // 避免长生命周期 scope 捕获 DbContext 导致跨执行数据污染/线程安全隐患。
                using var executionScope = _scopeFactory.CreateScope();
                var dbContext = executionScope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
                var executor = executionScope.ServiceProvider.GetRequiredService<WorkflowExecutor>();

                Workflow workflow;
                var preloaded = item.PreloadedWorkflow;
                if (preloaded is not null && preloaded.Id == item.WorkflowDefinitionId)
                {
                    // 复用调用方随工作项携带的已加载工作流，省去一次数据库查询。
                    workflow = preloaded;
                }
                else
                {
                    var loaded = await dbContext.Workflows
                        .FirstOrDefaultAsync(w => w.Id == item.WorkflowDefinitionId, stoppingToken)
                        .ConfigureAwait(false);

                    if (loaded is null)
                    {
                        _logger.LogWarning("工作流 {WorkflowId} 不存在，跳过执行。", item.WorkflowDefinitionId);
                        continue;
                    }

                    workflow = loaded;
                }

                // 每执行一个独立的、与进程关闭令牌联动的取消源，并登记到注册表；
                // CancelAsync 取消该源即可真正中断运行中的执行，worker 退出时解除登记。
                using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cancellationRegistry.Register(item.ExecutionRecordId, executionCts);
                try
                {
                    await executor.ExecuteLoopAsync(
                            workflow,
                            item.ExecutionRecordId,
                            item.TriggerPayload,
                            dbContext,
                            executionCts.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    cancellationRegistry.Unregister(item.ExecutionRecordId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("执行 {ExecutionId} 因应用关闭而取消。", item.ExecutionRecordId);
            }
            catch (OperationCanceledException)
            {
                // 用户经 CancelAsync 触发的取消（非进程关闭）：内核已将执行落库为 Cancelled，此处仅记录。
                _logger.LogInformation("执行 {ExecutionId} 已被用户取消。", item.ExecutionRecordId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行 {ExecutionId} 发生未处理异常。", item.ExecutionRecordId);

                try
                {
                    using var errorScope = _scopeFactory.CreateScope();
                    var dbContext = errorScope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
                    var record = await dbContext.ExecutionRecords
                        .FirstOrDefaultAsync(e => e.Id == item.ExecutionRecordId, stoppingToken)
                        .ConfigureAwait(false);
                    if (record is { Status: ExecutionStatus.Running or ExecutionStatus.Pending })
                    {
                        record.Status = ExecutionStatus.Failed;
                        record.CompletedAt = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "更新执行 {ExecutionId} 状态为 Failed 失败。", item.ExecutionRecordId);
                }
            }
        }
    }
}
