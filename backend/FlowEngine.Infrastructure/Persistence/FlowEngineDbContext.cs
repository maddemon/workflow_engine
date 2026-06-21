using FlowEngine.Core.Identity;
using FlowEngine.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Infrastructure.Persistence;

/// <summary>
/// FlowEngine 数据库上下文。
/// </summary>
public sealed class FlowEngineDbContext : DbContext
{
    /// <summary>
    /// 工作流定义数据集。
    /// </summary>
    public DbSet<WorkflowDefinitionEntity> WorkflowDefinitions => Set<WorkflowDefinitionEntity>();

    /// <summary>
    /// 执行记录数据集。
    /// </summary>
    public DbSet<ExecutionRecordEntity> ExecutionRecords => Set<ExecutionRecordEntity>();

    /// <summary>
    /// 节点执行记录数据集。
    /// </summary>
    public DbSet<NodeExecutionRecordEntity> NodeExecutionRecords => Set<NodeExecutionRecordEntity>();

    /// <summary>
    /// 凭据数据集。
    /// </summary>
    public DbSet<CredentialEntity> Credentials => Set<CredentialEntity>();

    /// <summary>
    /// 触发器数据集。
    /// </summary>
    public DbSet<TriggerEntity> Triggers => Set<TriggerEntity>();

    /// <summary>
    /// Webhook 路由数据集。
    /// </summary>
    public DbSet<WebhookRouteEntity> WebhookRoutes => Set<WebhookRouteEntity>();

    /// <summary>
    /// 用户数据集。
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// 用户角色数据集。
    /// </summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    /// <summary>
    /// 初始化数据库上下文。
    /// </summary>
    /// <param name="options">上下文选项。</param>
    public FlowEngineDbContext(DbContextOptions<FlowEngineDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CredentialEntity>(entity =>
        {
            entity.ToTable("credentials");
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.Type);
        });
    }
}
