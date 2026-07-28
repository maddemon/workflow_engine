using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using FlowEngine.Core.Data;

namespace FlowEngine.Migrations.Postgres;

/// <summary>
/// PostgreSQL 迁移设计期工厂。仅用于 <c>dotnet ef migrations add</c> / <c>database update</c>，
/// 生成 PostgreSQL 专用迁移（与 SQLite 迁移分处独立程序集，避免互相污染）。
/// 连接串通过环境变量 <c>FLOWENGINE_CONNECTION_STRING</c> 提供，缺省回退到本机测试实例。
/// </summary>
public class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlowEngineDbContext>
{
    public FlowEngineDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWENGINE_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=flowengine;Username=postgres;Password=password";

        var optionsBuilder = new DbContextOptionsBuilder<FlowEngineDbContext>();
        optionsBuilder.UseNpgsql(connectionString, x =>
            x.MigrationsAssembly("FlowEngine.Migrations.Postgres")
             .MigrationsHistoryTable("__ef_migrations_history", "flow"));

        return new FlowEngineDbContext(optionsBuilder.Options);
    }
}
