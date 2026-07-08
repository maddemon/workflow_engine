namespace FlowEngine.Core.Scripting;

/// <summary>
/// JS 脚本引擎安全限制配置。
/// </summary>
public sealed class JsEngineOptions
{
    /// <summary>
    /// 脚本执行超时（毫秒）。默认 5000ms。
    /// </summary>
    public int ExecutionTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// 内存限制（字节）。默认 8MB。
    /// </summary>
    public long MemoryLimitBytes { get; set; } = 8_000_000;

    /// <summary>
    /// 最大语句数。默认 5000。
    /// </summary>
    public int MaxStatements { get; set; } = 5000;

    /// <summary>
    /// 递归深度限制。默认 50。
    /// </summary>
    public int RecursionDepthLimit { get; set; } = 50;

    /// <summary>
    /// 正则表达式超时（毫秒）。默认 2000ms。
    /// </summary>
    public int RegexTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// 数组大小限制。默认 100000。
    /// </summary>
    public int ArraySizeLimit { get; set; } = 100_000;
}
