using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using FlowEngine.Core.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FlowEngine.Core.Data;

/// <summary>
/// FlowEngine 数据库上下文。
/// </summary>
public class FlowEngineDbContext : DbContext
{
    public DbSet<Workflow> Workflows => Set<Workflow>();

    public DbSet<ExecutionRecord> ExecutionRecords => Set<ExecutionRecord>();

    public DbSet<Credential> Credentials => Set<Credential>();

    public DbSet<Trigger> Triggers => Set<Trigger>();

    public DbSet<WebhookRoute> WebhookRoutes => Set<WebhookRoute>();

    public DbSet<User> Users => Set<User>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    public DbSet<ExecutionDedup> ExecutionDedups => Set<ExecutionDedup>();

    public DbSet<WorkflowCredentialUsage> WorkflowCredentialUsages => Set<WorkflowCredentialUsage>();

    public FlowEngineDbContext(DbContextOptions<FlowEngineDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// 保存更改。凭据引用关联表（<see cref="WorkflowCredentialUsage"/>）的同步由
    /// <see cref="WorkflowCredentialUsageInterceptor"/> 在 SaveChanges 事务内部完成，
    /// 因此凭据引用行的删除与写入同工作流主体数据在同一事务内原子提交。本方法不再承载该业务逻辑。
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess = true)
    {
        BumpOptimisticConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// 保存更改（异步）。在提交前自增高竞争实体的乐观并发行版本，详见
    /// <see cref="BumpOptimisticConcurrencyTokens"/>。
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        BumpOptimisticConcurrencyTokens();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 为携带 <c>RowVersion</c> 乐观并发令牌的实体在每次新增或变更时自增版本号，
    /// 使跨 DbContext 的并发更新可被乐观并发检测拦截（防止丢失更新）。
    /// </summary>
    /// <remarks>
    /// <para>为何由应用层维护而非依赖数据库 rowversion：SQLite/PostgreSQL/MySQL 不会在
    /// UPDATE 时自动递增 <see cref="byte"/>[] rowversion，仅 SQL Server 原生支持。
    /// 采用 <see cref="long"/> 令牌并在保存前统一自增，可保证所有提供程序行为一致。</para>
    /// <para>仅处理 <see cref="EntityState.Added"/> 与 <see cref="EntityState.Modified"/> 实体；
    /// 未变更实体不进入此分支，避免无谓的版本自增。</para>
    /// </remarks>
    private void BumpOptimisticConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var property = entry.Metadata.FindProperty("RowVersion");
            if (property is null)
            {
                continue;
            }

            var current = entry.Property(property.Name).CurrentValue;
            var next = current is null ? 1L : Convert.ToInt64(current) + 1;
            entry.Property(property.Name).CurrentValue = next;
        }
    }

    /// <summary>
    /// 注册 <see cref="WorkflowCredentialUsageInterceptor"/>，使其在每次 SaveChanges 时于事务内部
    /// 维护凭据引用关联表。无论 DbContext 经 DI 还是直接构造，拦截器均生效。
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(new WorkflowCredentialUsageInterceptor());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // D-1：为所有派生自 Entity 的映射实体配置全局软删除过滤器 !e.Deleted，
        // 软删除行默认不可见；需在查询已删数据处显式 IgnoreQueryFilters()。
        ApplySoftDeleteQueryFilters(modelBuilder);

        // D-2/D-3：触发器关联查询索引（按工作流定义 / 项目过滤）。
        modelBuilder.Entity<Trigger>().HasIndex(t => t.WorkflowDefinitionId);
        modelBuilder.Entity<Trigger>().HasIndex(t => t.ProjectId);

        // D-4：存储文件按项目查询索引。
        modelBuilder.Entity<StoredFile>().HasIndex(f => f.ProjectId);

        // D-15：凭据唯一约束跨库一致——SQLite 与 PostgreSQL 对 (Name, NULL) 的语义分歧
        // 通过两个过滤唯一索引统一：项目内唯一、全局（NULL）唯一；NULL 不再被多个库区别对待。
        modelBuilder.Entity<Credential>()
            .HasIndex(e => new { e.Name, e.ProjectId })
            .IsUnique()
            .HasFilter("\"project_id\" IS NOT NULL")
            .HasDatabaseName("IX_credentials_name_project_id_notnull");
        modelBuilder.Entity<Credential>()
            .HasIndex(e => e.Name)
            .IsUnique()
            .HasFilter("\"project_id\" IS NULL")
            .HasDatabaseName("IX_credentials_name_null_project");

        modelBuilder.Entity<ExecutionDedup>().HasIndex(e => e.IdempotencyKey).IsUnique();

        // 必须在遍历 modelBuilder.Model 之前显式配置带 [JsonColumn] 的属性，
        // 否则 EF Core 会对 Dictionary<,>/List<> 等泛型 navigation 进行关联探测并抛出
        // "Unable to determine the relationship" 异常。下方法基于 CLR 反射调用
        // EntityTypeBuilder.Property(...).HasConversion(...) Fluent API，避免触发模型 finalization。
        ConfigureJsonColumns(modelBuilder);
    }

    /// <summary>
    /// 为所有派生自 <see cref="Entity"/> 的映射实体类型配置全局软删除查询过滤器
    /// <c>!e.Deleted</c>，使软删除行在常规查询中默认不可见。需要在查询已删除数据处
    /// 显式调用 <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/> 关闭过滤。
    /// </summary>
    private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType == typeof(Entity) || !typeof(Entity).IsAssignableFrom(clrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(clrType, "e");
            var body = Expression.Not(Expression.Property(parameter, nameof(Entity.Deleted)));
            var filter = Expression.Lambda(body, parameter);

            modelBuilder.Entity(clrType).Metadata.SetQueryFilter(filter);
        }
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
        // 注意：OnModelCreating 阶段（含设计期迁移脚手架）Database.ProviderName 尚未赋值，
        // 因此改从已配置完成的 DbContextOptions 扩展判断提供程序，保证运行时与设计期一致。
        var columnType = IsNpgsqlProvider() ? "jsonb" : "json";
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

    /// <summary>
    /// 判断当前提供程序是否为 PostgreSQL（Npgsql）。
    /// 不直接引用 Npgsql 类型以保持 Core 与具体提供程序解耦，改为按扩展类型全名匹配。
    /// </summary>
    private bool IsNpgsqlProvider()
    {
        var options = this.GetService<IDbContextOptions>();
        return options.Extensions.Any(e =>
            e.GetType().FullName?.IndexOf("pgsql", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}


