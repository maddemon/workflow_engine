namespace FlowEngine.Core.Scripting;

/// <summary>
/// n8n 式 $input 输入容器。在表达式中通过 <c>$input.item()</c>/<c>all()</c>/<c>first()</c>/<c>last()</c>/<c>params</c>/<c>context</c> 访问。
/// <c>$input.item()</c> 等价于 <c>$json</c>（当前 item 数据）。
/// </summary>
public sealed class InputContainer
{
    private readonly List<object?> _allItems;
    private readonly object? _currentItem;
    private readonly IReadOnlyDictionary<string, object>? _params;
    private readonly IReadOnlyDictionary<string, object?>? _context;

    /// <summary>
    /// 初始化 <see cref="InputContainer"/>。
    /// </summary>
    /// <param name="allItems">当前节点全部输入 item 数组。</param>
    /// <param name="currentItem">当前 item（等于 <c>allItems[runIndex]</c>，仅在逐项执行时有意义）。</param>
    /// <param name="params">当前节点参数（可选）。</param>
    /// <param name="context">执行上下文数据（可选）。</param>
    public InputContainer(
        List<object?> allItems,
        object? currentItem,
        IReadOnlyDictionary<string, object>? @params = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        _allItems = allItems ?? throw new ArgumentNullException(nameof(allItems));
        _currentItem = currentItem;
        _params = @params;
        _context = context;
    }

    /// <summary>
    /// 当前 item 数据（等价于 <c>$json</c>）。
    /// </summary>
    public object? item() => _currentItem;

    /// <summary>
    /// 当前节点全部输入 item 数组。
    /// </summary>
    public List<object?> all() => _allItems;

    /// <summary>
    /// 第一个输入 item。
    /// </summary>
    public object? first() => _allItems.FirstOrDefault();

    /// <summary>
    /// 最后一个输入 item。
    /// </summary>
    public object? last() => _allItems.LastOrDefault();

    /// <summary>
    /// 输入 item 数量。
    /// </summary>
    public int count() => _allItems.Count;

    /// <summary>
    /// 当前节点参数字典。
    /// </summary>
    public IReadOnlyDictionary<string, object>? Params => _params;

    /// <summary>
    /// 执行上下文信息（含 executionId、runIndex 等）。
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Context => _context;
}
