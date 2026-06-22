using FlowEngine.Core.Data;
using FlowEngine.Runtime.Executor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Host.Executor;

/// <summary>
/// 工作流执行后台服务，从 <see cref="WorkflowExecutionQueue"/> 读取工作项并驱动 <see cref="WorkflowExecutor"/> 执行。
/// 使用 <see cref="IHostApplicationLifetime.ApplicationStopping"/> 作为取消令牌，确保应用关闭时优雅终止。
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
        using var scope = _scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<WorkflowExecutionQueue>();
        var executor = scope.ServiceProvider.GetRequiredService<WorkflowExecutor>();

        while (!stoppingToken.IsCancellationRequested)
        {
            WorkflowExecutionWorkItem item;
            try
            {
                item = await queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var executionScope = _scopeFactory.CreateScope();
                var dbContext = executionScope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

                var workflow = await dbContext.Workflows
                    .FirstOrDefaultAsync(w => w.Id == item.WorkflowDefinitionId, stoppingToken)
                    .ConfigureAwait(false);

                if (workflow is null)
                {
                    _logger.LogWarning("工作流 {WorkflowId} 不存在，跳过执行。", item.WorkflowDefinitionId);
                    continue;
                }

                await executor.ExecuteLoopAsync(
                        workflow,
                        item.ExecutionRecordId,
                        item.TriggerPayload,
                        dbContext,
                        stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("执行 {ExecutionId} 因应用关闭而取消。", item.ExecutionRecordId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行 {ExecutionId} 发生未处理异常。", item.ExecutionRecordId);
            }
        }
    }
}
