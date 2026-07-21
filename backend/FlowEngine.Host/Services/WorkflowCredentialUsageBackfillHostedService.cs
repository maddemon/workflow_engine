using FlowEngine.Application.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Host.Services;

/// <summary>
/// 启动时一次性回填 <c>workflow_credential_usages</c>，补齐迁移前已存在工作流的引用行。
/// 已回填的工作流在下一次启动时会被「是否存在引用行」的廉价检查跳过，故不会重复扫描。
/// 回填失败不应阻断应用启动。
/// </summary>
public sealed class WorkflowCredentialUsageBackfillHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowCredentialUsageBackfillHostedService> logger)
    : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var backfill = scope.ServiceProvider.GetRequiredService<WorkflowCredentialUsageBackfill>();
            var count = await backfill.BackfillAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("工作流凭据引用关系回填完成，共处理 {Count} 个工作流", count);
        }
        catch (OperationCanceledException)
        {
            // 应用关闭导致取消，忽略。
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "工作流凭据引用关系回填失败");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
