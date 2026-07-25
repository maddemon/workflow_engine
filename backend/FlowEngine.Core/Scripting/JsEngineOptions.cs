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
    /// 沙箱白名单（SEC-7）：仅放行执行工作流脚本所必须的 JS 全局 API 与注入的安全辅助函数。
    /// 引擎创建时，<see cref="JsEngine"/> 会删除全局对象上所有<b>不在</b>本集合内的自有属性，
    /// 从而从源头消除 <c>process</c>/<c>require</c>/<c>globalThis</c> 等危险标识符，
    /// 即使通过 <c>this['cons'+'tructor']</c> 之类的字符串拼接绕过黑名单也无法访问 CLR（未启用 AllowClr）。
    /// 黑名单 <see cref="ForbiddenIdentifiers"/> 仍作为纵深防御兜底。
    /// </summary>
    private static readonly HashSet<string> s_defaultAllowedGlobals = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── 安全 JS 内置（仅保留工作流脚本常用的纯计算/数据结构 API）──
        "Array", "Boolean", "Date", "Error", "EvalError", "Function", "Infinity", "JSON", "Map",
        "Math", "NaN", "Number", "Object", "Promise", "Proxy", "RangeError", "ReferenceError",
        "RegExp", "Set", "String", "Symbol", "SyntaxError", "TypeError", "URIError", "WeakMap",
        "WeakSet", "parseInt", "parseFloat", "isNaN", "isFinite", "decodeURI", "encodeURI",
        "decodeURIComponent", "encodeURIComponent",
        // ── 本引擎注入的安全辅助函数（注入后始终存在，不依赖白名单）──
        "console", "now", "nowIso", "jmespath", "length", "trim",
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

    /// <summary>
    /// 沙箱白名单（SEC-7）：仅允许存在的全局标识符集合。
    /// 引擎会删除全局对象上所有不在本集合内的自有属性，作为防逃逸主防线；
    /// 黑名单 <see cref="ForbiddenIdentifiers"/> 仅作纵深防御兜底。
    /// </summary>
    public IReadOnlySet<string> AllowedGlobals { get; set; } = s_defaultAllowedGlobals;
}
