namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 执行日志记录器。
/// </summary>
public interface IExecutionLogger
{
    /// <summary>
    /// 记录信息日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    /// <param name="args">消息参数。</param>
    void LogInformation(string message, params object?[] args);

    /// <summary>
    /// 记录警告日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    /// <param name="args">消息参数。</param>
    void LogWarning(string message, params object?[] args);

    /// <summary>
    /// 记录错误日志。
    /// </summary>
    /// <param name="exception">异常信息。</param>
    /// <param name="message">日志消息。</param>
    /// <param name="args">消息参数。</param>
    void LogError(Exception? exception, string message, params object?[] args);
}
