namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点上下文扩展方法，提供类型安全的读写操作。
/// </summary>
public static class NodeContextExtensions
{
    /// <summary>获取类型安全的值。T 约束为 class 避免值类型拆箱语义混乱；值类型请用 <see cref="GetValue"/>。</summary>
    public static T? Get<T>(this IDictionary<string, object?> context, string key) where T : class
    {
        if (context.TryGetValue(key, out var value) && value is T typed)
        {
            return typed;
        }

        return default;
    }

    /// <summary>设置值（引用类型或值类型均可）。</summary>
    public static void Set<T>(this IDictionary<string, object?> context, string key, T value)
    {
        context[key] = value;
    }

    /// <summary>尝试获取类型安全的值（引用类型）。</summary>
    public static bool TryGet<T>(this IDictionary<string, object?> context, string key, out T? value) where T : class
    {
        if (context.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>获取或添加（引用类型）。</summary>
    public static T GetOrAdd<T>(this IDictionary<string, object?> context, string key, Func<T> factory) where T : class
    {
        if (context.TryGetValue(key, out var value) && value is T typed)
        {
            return typed;
        }

        var newValue = factory();
        context[key] = newValue;
        return newValue;
    }

    /// <summary>非泛型读取：覆盖 int/double/bool 等值类型（强类型 <see cref="Get{T}"/> 受 <c>where T : class</c> 约束无法覆盖）。缺失或类型不符时返回 null。</summary>
    public static object? GetValue(this IDictionary<string, object?> context, string key)
        => context.TryGetValue(key, out var value) ? value : null;

    /// <summary>非泛型写入：与 <see cref="GetValue"/> 配对，供值类型状态（计数器、游标、位置、页码）使用。</summary>
    public static void SetValue(this IDictionary<string, object?> context, string key, object? value)
        => context[key] = value;
}
