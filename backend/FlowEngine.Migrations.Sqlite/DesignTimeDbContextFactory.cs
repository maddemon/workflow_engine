using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using FlowEngine.Core.Data;

namespace FlowEngine.Migrations.Sqlite;

/// <summary>
/// SQLite 迁移设计期工厂。仅用于 <c>dotnet ef migrations add</c> / <c>database update</c>，
/// 生成 SQLite 专用迁移（与 PostgreSQL 迁移分处独立程序集，避免互相污染）。
/// 连接串通过环境变量 <c>FLOWENGINE_CONNECTION_STRING</c> 提供，缺省回退到本地文件库。
/// </summary>
public class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlowEngineDbContext>
{
    public FlowEngineDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWENGINE_CONNECTION_STRING")
            ?? "Data Source=flowengine.db";

        var optionsBuilder = new DbContextOptionsBuilder<FlowEngineDbContext>();
        optionsBuilder.UseSqlite(connectionString, x =>
            x.MigrationsAssembly("FlowEngine.Migrations.Sqlite")
             .MigrationsHistoryTable("__ef_migrations_history"));

        return new FlowEngineDbContext(optionsBuilder.Options);
    }
}
