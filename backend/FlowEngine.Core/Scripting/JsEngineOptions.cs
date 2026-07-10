namespace FlowEngine.Core.Scripting;

/// <summary>
/// JS 脚本引擎安全限制配置。
/// </summary>
public sealed class JsEngineOptions
{
    private static readonly HashSet<string> s_defaultForbiddenIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "require", "process", "fs", "path", "os", "net", "http", "https",
        "fetch", "XMLHttpRequest", "WebSocket", "eval",
        "setTimeout", "setInterval", "setImmediate", "clearTimeout", "clearInterval",
        "globalThis", "window", "document", "constructor", "prototype", "__proto__",
        "import", "export", "module", "exports",
    };

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

    /// <summary>
    /// 表达式/脚本中禁止使用的标识符集合。
    /// 默认包含 require、process、fs、fetch、eval 等可能用于逃逸沙箱的符号。
    /// </summary>
    public IReadOnlySet<string> ForbiddenIdentifiers { get; set; } = s_defaultForbiddenIdentifiers;
}
