using System.ComponentModel.DataAnnotations.Schema;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 重试策略。
/// </summary>
[NotMapped]
public class RetryPolicy
{
    /// <summary>
    /// 最大重试次数。
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// 基础延迟。
    /// </summary>
    public TimeSpan BaseDelay { get; set; }

    /// <summary>
    /// 最大延迟。
    /// </summary>
    public TimeSpan MaxDelay { get; set; }

    /// <summary>
    /// 是否使用抖动。
    /// </summary>
    public bool UseJitter { get; set; }
}
