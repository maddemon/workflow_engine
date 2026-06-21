using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FlowEngine.Core.Data;

namespace FlowEngine.Migrations;

public static class MigrationsExtensions
{
    public static async Task ApplyFlowEngineMigrationsAsync(
        this IServiceProvider serviceProvider,
        string provider,
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

            if (provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
            {
                await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                logger?.LogDebug("SQLite WAL 模式已启用");
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "数据库迁移执行失败");
            throw;
        }
    }
}
