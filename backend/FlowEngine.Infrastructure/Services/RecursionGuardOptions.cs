namespace FlowEngine.Infrastructure.Services;

/// <summary>
/// 递归深度保护选项。
/// </summary>
public sealed class RecursionGuardOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "RecursionGuard";

    /// <summary>
    /// 允许的最大递归深度（含）。超过则 <see cref="IRecursionGuard.TryEnter"/> 返回 false。
    /// 默认值 100，仅作安全网；正常节点依赖自身终止条件。
    /// </summary>
    public int MaxDepth { get; set; } = 100;
}
