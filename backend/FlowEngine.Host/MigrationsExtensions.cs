using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FlowEngine.Core.Data;

namespace FlowEngine.Host;

/// <summary>
/// 运行时迁移执行器。在宿主启动时调用，按当前 provider 应用待执行的 EF Core 迁移。
/// 放在宿主项目中（而非迁移程序集），因为它属于启动编排逻辑，而非迁移定义本身。
/// </summary>
public static class MigrationsExtensions
{
    public static async Task ApplyFlowEngineMigrationsAsync(
        this IServiceProvider serviceProvider,
        ILogger? logger = null)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        try
        {
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();

            if (pendingMigrations.Any())
            {
                logger?.LogInformation(
                    "检测到 {Count} 个待执行的数据库迁移",
                    pendingMigrations.Count());

                await dbContext.Database.MigrateAsync();

                logger?.LogInformation("数据库迁移执行完成");
            }
            else
            {
                logger?.LogInformation("数据库已是最新状态，无需迁移");
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "数据库迁移执行失败");
            throw;
        }
    }
}
