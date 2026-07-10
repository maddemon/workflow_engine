using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// 脚本执行上下文，封装节点执行上下文与额外全局变量。
/// </summary>
public sealed class ScriptContext
{
    /// <summary>
    /// 节点执行上下文。
    /// </summary>
    public NodeExecutionContext NodeContext { get; }

    /// <summary>
    /// 额外全局变量，将在脚本执行前注入引擎。
    /// </summary>
    public IReadOnlyDictionary<string, object?> ExtraGlobals { get; }

    /// <summary>
    /// 初始化 <see cref="ScriptContext"/>。
    /// </summary>
    public ScriptContext(NodeExecutionContext nodeContext, IReadOnlyDictionary<string, object?>? extraGlobals = null)
    {
        NodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        ExtraGlobals = extraGlobals ?? new Dictionary<string, object?>();
    }

    /// <summary>
    /// 从节点执行上下文创建脚本上下文。
    /// </summary>
    public static ScriptContext From(NodeExecutionContext nodeContext)
        => new(nodeContext);
}
