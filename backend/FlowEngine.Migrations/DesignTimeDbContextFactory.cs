using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MySql.EntityFrameworkCore.Extensions;
using FlowEngine.Core.Data;

namespace FlowEngine.Migrations;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlowEngineDbContext>
{
    public FlowEngineDbContext CreateDbContext(string[] args)
    {
        var provider = GetProviderFromArgs(args);
        var optionsBuilder = new DbContextOptionsBuilder<FlowEngineDbContext>();

        ConfigureProvider(optionsBuilder, provider);

        return new FlowEngineDbContext(optionsBuilder.Options);
    }

    private static string GetProviderFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--provider")
                return args[i + 1];
        }

        return Environment.GetEnvironmentVariable("FLOWENGINE_DB_PROVIDER") ?? "sqlite";
    }

    private static void ConfigureProvider(
        DbContextOptionsBuilder<FlowEngineDbContext> builder,
        string provider)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWENGINE_CONNECTION_STRING");

        switch (provider.ToLowerInvariant())
        {
            case "sqlite":
                connectionString ??= "Data Source=flowengine.db";
                builder.UseSqlite(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history"));
                break;

            case "postgresql":
            case "npgsql":
                connectionString ??= "Host=localhost;Database=flowengine;Username=postgres;Password=password";
                builder.UseNpgsql(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history", "flow"));
                break;

            case "mysql":
            case "pomelo":
                connectionString ??= "Server=localhost;Database=flowengine;User=root;Password=password";
                builder.UseMySQL(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history"));
                break;

            case "tidb":
                connectionString ??= "Server=localhost;Port=4000;Database=flowengine;User=root;Password=";
                builder.UseMySQL(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history"));
                break;

            case "oceanbase":
                connectionString ??= "Server=localhost;Port=2881;Database=flowengine;User=root@mysql_tenant;Password=password";
                builder.UseMySQL(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history"));
                break;

            case "dameng":
            case "dm":
                connectionString ??= "Server=localhost;User Id=SYSDBA;Password=SYSDBA;Port=5236";
                builder.UseDm(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history"));
                break;

            case "kingbasees":
            case "kingbase":
                connectionString ??= "Host=localhost;Database=flowengine;Username=system;Password=123456;Port=54321";
                builder.UseNpgsql(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history", "flow"));
                break;

            default:
                throw new ArgumentException($"Unsupported database provider: {provider}");
        }
    }
}
