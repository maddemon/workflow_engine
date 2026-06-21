using System.Reflection;
using System.Text.Json;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using FlowEngine.Core.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FlowEngine.Core.Data;

/// <summary>
/// FlowEngine 数据库上下文。
/// </summary>
public sealed class FlowEngineDbContext : DbContext
{
    public DbSet<Workflow> Workflows => Set<Workflow>();

    public DbSet<ExecutionRecord> ExecutionRecords => Set<ExecutionRecord>();

    public DbSet<Credential> Credentials => Set<Credential>();

    public DbSet<Trigger> Triggers => Set<Trigger>();

    public DbSet<WebhookRoute> WebhookRoutes => Set<WebhookRoute>();

    public DbSet<User> Users => Set<User>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public FlowEngineDbContext(DbContextOptions<FlowEngineDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 必须在遍历 modelBuilder.Model 之前显式配置带 [JsonColumn] 的属性，
        // 否则 EF Core 会对 Dictionary<,>/List<> 等泛型 navigation 进行关联探测并抛出
        // "Unable to determine the relationship" 异常。下方法基于 CLR 反射调用
        // EntityTypeBuilder.Property(...).HasConversion(...) Fluent API，避免触发模型 finalization。
        ConfigureJsonColumns(modelBuilder);
    }

    /// <summary>
    /// 扫描实体程序集中所有标记 <see cref="JsonColumnAttribute"/> 的属性，统一配置：
    /// <list type="bullet">
    /// <item>列类型为 <c>jsonb</c>（PostgreSQL）或 <c>json</c>（其他 Provider）。</item>
    /// <item>使用 <see cref="JsonValueConverter{T}"/> 将 CLR 类型与 JSON 字符串互转。</item>
    /// </list>
    /// 通过 Fluent API 显式配置，可阻止 EF Core 对 <see cref="Dictionary{TKey,TValue}"/>
    /// 或 <see cref="List{T}"/> 等泛型属性进行关联探测。
    /// </summary>
    private void ConfigureJsonColumns(ModelBuilder modelBuilder)
    {
        var columnType = Database.ProviderName == "Npgsql" ? "jsonb" : "json";
        var entityTypes = typeof(Workflow).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !typeof(Attribute).IsAssignableFrom(t));

        foreach (var clrType in entityTypes)
        {
            var jsonProperties = clrType.GetProperties()
                .Where(p => p.GetCustomAttribute<JsonColumnAttribute>() is not null)
                .ToList();
            if (jsonProperties.Count == 0)
            {
                continue;
            }

            var entityBuilder = modelBuilder.Entity(clrType);
            foreach (var property in jsonProperties)
            {
                var propertyBuilder = entityBuilder.Property(property.Name);
                propertyBuilder.HasConversion(JsonValueConverter.Create(property.PropertyType));
                propertyBuilder.HasColumnType(columnType);
            }
        }
    }
}


