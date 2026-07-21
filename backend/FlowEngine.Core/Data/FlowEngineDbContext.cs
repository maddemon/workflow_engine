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
    /// 集中维护 <see cref="WorkflowCredentialUsage"/> 关联表：在工作流新增/修改/删除时，
    /// 删除该工作流的旧引用行，并为新增/修改的工作流按节点参数重新计算写入引用行。
    /// 删除+写入在 <see cref="base.SaveChangesAsync"/> 的同一事务内原子提交。
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var changedWorkflows = ChangeTracker.Entries<Workflow>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (changedWorkflows.Count > 0)
        {
            await MaintainWorkflowCredentialUsagesAsync(changedWorkflows, cancellationToken).ConfigureAwait(false);
        }

        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MaintainWorkflowCredentialUsagesAsync(
        IReadOnlyList<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Workflow>> changedWorkflows,
        CancellationToken cancellationToken)
    {
        var workflowIds = changedWorkflows.Select(e => e.Entity.Id).ToList();

        // 先删除这些工作流的全部旧引用行（新增/修改会随后重写；删除则仅清理孤儿行）。
        var existing = await WorkflowCredentialUsages
            .Where(u => workflowIds.Contains(u.WorkflowId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing.Count > 0)
        {
            WorkflowCredentialUsages.RemoveRange(existing);
        }

        // 仅为仍存在的（新增/修改）工作流重新计算并写入引用行。
        foreach (var entry in changedWorkflows)
        {
            if (entry.State == EntityState.Deleted)
            {
                continue;
            }

            foreach (var usage in CredentialReferenceScanner.Scan(entry.Entity))
            {
                WorkflowCredentialUsages.Add(usage);
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ExecutionDedup>().HasIndex(e => e.IdempotencyKey).IsUnique();
        modelBuilder.Entity<Credential>().HasIndex(e => new { e.Name, e.ProjectId }).IsUnique();

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


