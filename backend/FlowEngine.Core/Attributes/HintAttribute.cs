using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Attributes;

/// <summary>
/// 覆盖默认的 <see cref="PresentationHint"/> 推断，指定前端渲染组件。
/// </summary>
/// <example>
/// <code>
/// [Hint(PresentationHint.CodeEditor)]
/// public string Code { get; set; } = string.Empty;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HintAttribute : Attribute
{
    /// <summary>
    /// 渲染提示。
    /// </summary>
    public PresentationHint Hint { get; }

    /// <summary>
    /// 指定渲染提示。
    /// </summary>
    /// <param name="hint">渲染提示枚举值。</param>
    public HintAttribute(PresentationHint hint)
    {
        Hint = hint;
    }
}
