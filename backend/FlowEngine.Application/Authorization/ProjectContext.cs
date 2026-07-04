namespace FlowEngine.Application.Authorization;

/// <summary>
/// 项目上下文的 Scoped 实现，仅保存当前请求指定的项目标识（用于分类筛选）。
/// </summary>
public sealed class ProjectContext : IProjectContext
{
    /// <inheritdoc/>
    public Guid? CurrentProjectId { get; set; }
}
