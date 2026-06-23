using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Attributes;

/// <summary>
/// 指定前端渲染组件及其配置。
/// Hint 和 Props 都可选：Hint 根据属性类型自动推断，Props 传递额外配置给组件。
/// </summary>
/// <example>
/// <code>
/// // 自动推断渲染组件
/// public string Name { get; set; } = string.Empty;
/// 
/// // 指定渲染组件
/// [Hint(PresentationHint.CodeEditor)]
/// public string Code { get; set; } = string.Empty;
/// 
/// // 指定渲染组件 + 扩展属性
/// [Hint(PresentationHint.Script, "language", ScriptLanguage.JavaScript)]
/// public string Condition { get; set; } = string.Empty;
/// 
/// // 仅指定扩展属性（自动推断 Hint）
/// [Hint("language", ScriptLanguage.JavaScript)]
/// public string ScriptCode { get; set; } = string.Empty;
/// 
/// // 数组子项配置
/// [Hint(PresentationHint.Array, "itemType", typeof(SwitchCase))]
/// public List&lt;SwitchCase&gt; Cases { get; set; } = [];
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HintAttribute : Attribute
{
    /// <summary>
    /// 渲染提示（null 表示自动推断）。
    /// </summary>
    public PresentationHint? Component { get; }

    /// <summary>
    /// 扩展属性，供 Hint 组件使用。
    /// 键值对格式：key1, value1, key2, value2, ...
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; }

    /// <summary>
    /// 自动推断渲染提示，无扩展属性。
    /// </summary>
    public HintAttribute()
    {
        Properties = new Dictionary<string, object>();
    }

    /// <summary>
    /// 指定渲染提示，无扩展属性。
    /// </summary>
    /// <param name="render">渲染提示枚举值。</param>
    public HintAttribute(PresentationHint render)
    {
        Component = render;
        Properties = new Dictionary<string, object>();
    }

    /// <summary>
    /// 自动推断渲染提示，带扩展属性。
    /// </summary>
    /// <param name="props">键值对，交替传入 key 和 value。</param>
    public HintAttribute(params object[] props)
    {
        Properties = ParseProps(props);
    }

    /// <summary>
    /// 指定渲染提示和扩展属性。
    /// </summary>
    /// <param name="Hint">渲染提示枚举值。</param>
    /// <param name="props">键值对，交替传入 key 和 value。</param>
    public HintAttribute(PresentationHint component, params object[] props)
    {
        Component = component;
        Properties = ParseProps(props);
    }

    private static IReadOnlyDictionary<string, object> ParseProps(object[] props)
    {
        var dict = new Dictionary<string, object>();
        for (var i = 0; i < props.Length - 1; i += 2)
        {
            if (props[i] is string key && props[i + 1] is not null)
            {
                dict[key] = props[i + 1];
            }
        }
        return dict;
    }
}
