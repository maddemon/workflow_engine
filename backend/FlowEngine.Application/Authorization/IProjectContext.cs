namespace FlowEngine.Application.Authorization;

/// <summary>
/// 项目上下文接口，提供请求级别的当前项目标识（用于分类筛选，不用于数据隔离）。
/// </summary>
public interface IProjectContext
{
    /// <summary>
    /// 当前请求指定的项目 ID（可能为 null 表示未指定项目）。
    /// </summary>
    Guid? CurrentProjectId { get; set; }
}
