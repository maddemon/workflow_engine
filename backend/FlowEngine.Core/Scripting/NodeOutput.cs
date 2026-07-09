namespace FlowEngine.Core.Scripting;

/// <summary>
/// 节点输出封装，供 <c>$node['NodeName']</c> 在表达式中访问。
/// 含 <c>.json</c>（数据项数组）、<c>.params</c>、<c>.context</c>、<c>.runIndex</c>。
/// </summary>
public sealed class NodeOutput
{
    /// <summary>
    /// 节点输出的数据项数组（每个元素为对应 DataItem.Data 的值）。
    /// </summary>
    public List<object?> Json { get; }

    /// <summary>
    /// 节点执行时的参数字典。
    /// </summary>
    public IReadOnlyDictionary<string, object>? Params { get; }

    /// <summary>
    /// 节点执行上下文信息。
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Context { get; }

    /// <summary>
    /// 节点执行时的 runIndex。
    /// </summary>
    public int RunIndex { get; }

    /// <summary>
    /// 初始化 <see cref="NodeOutput"/>。
    /// </summary>
    /// <param name="json">节点输出的数据项数组。</param>
    /// <param name="params">节点执行时的参数字典（可选）。</param>
    /// <param name="context">节点执行上下文信息（可选）。</param>
    /// <param name="runIndex">节点执行时的 runIndex。</param>
    public NodeOutput(
        List<object?> json,
        IReadOnlyDictionary<string, object>? @params = null,
        IReadOnlyDictionary<string, object?>? context = null,
        int runIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(json);
        Json = json;
        Params = @params;
        Context = context;
        RunIndex = runIndex;
    }
}
