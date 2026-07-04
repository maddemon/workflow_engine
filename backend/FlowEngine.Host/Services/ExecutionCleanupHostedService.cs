using FlowEngine.Application.ExecutionCleanup;
using FlowEngine.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Host.Services;

/// <summary>
/// 执行清理后台服务，定期清理过期的执行记录。
/// </summary>
public sealed class ExecutionCleanupHostedService(
    IServiceProvider serviceProvider,
    IOptions<ExecutionCleanupOptions> options,
    ILogger<ExecutionCleanupHostedService> logger) : BackgroundService
{
    private readonly ExecutionCleanupOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("执行清理服务已禁用。");
            return;
        }

        logger.LogInformation(
            "执行清理服务已启动，间隔 {IntervalMinutes} 分钟，保留 {RetentionDays} 天，每工作流最多 {MaxRecordsToKeep} 条记录。",
            _options.IntervalMinutes,
            _options.RetentionDays,
            _options.MaxRecordsToKeep);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var cleanupService = scope.ServiceProvider.GetRequiredService<ExecutionCleanupService>();
                await cleanupService.CleanupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "执行清理过程中发生错误。");
            }
        }
    }
}
