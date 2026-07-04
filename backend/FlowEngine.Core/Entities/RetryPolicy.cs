using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Enums;

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

    /// <summary>
    /// 退避策略。
    /// </summary>
    public BackoffStrategy BackoffStrategy { get; set; } = BackoffStrategy.Exponential;

    /// <summary>
    /// 可重试的错误码列表。为 null 或空时表示所有错误均可重试。
    /// </summary>
    public List<string>? RetryableErrorCodes { get; set; }
}
