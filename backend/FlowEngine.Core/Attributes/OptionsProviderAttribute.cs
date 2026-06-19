namespace FlowEngine.Core.Attributes;

/// <summary>
/// 指定动态选项源方法名，用于生成 <see cref="Entities.ParameterDefinition.Options"/> 列表。
/// 方法签名约定：<c>IEnumerable&lt;Option&gt; MethodName()</c>，
/// 也支持异步版本 <c>Task&lt;IEnumerable&lt;Option&gt;&gt; MethodName()</c>。
/// 调用时机为注册时（静态选项）。
/// </summary>
/// <example>
/// <code>
/// [OptionsProvider(nameof(GetDepartments))]
/// public string Department { get; set; } = string.Empty;
///
/// private IEnumerable&lt;Option&gt; GetDepartments() => [...];
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionsProviderAttribute : Attribute
{
    /// <summary>
    /// 提供选项的方法名（同一类上的实例方法）。
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// 指定选项源方法。
    /// </summary>
    /// <param name="methodName">方法名（使用 <c>nameof</c>）。</param>
    public OptionsProviderAttribute(string methodName)
    {
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
    }
}
