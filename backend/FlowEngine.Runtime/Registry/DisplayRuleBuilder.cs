using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 从 DisplayConditionAttribute 列表构建显示规则。
/// </summary>
internal static class DisplayRuleBuilder
{
    public static DisplayRule Build(List<DisplayConditionAttribute> conditions)
    {
        var fragments = new List<string>();
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var condition in conditions)
        {
            var camelProp = ParameterDiscoverer.ToCamelCase(condition.PropertyName);
            dependencies.Add(camelProp);

            // Boolean 值转为小写字符串
            var valueStr = condition.Value is bool b
                ? b.ToString().ToLowerInvariant()
                : condition.Value?.ToString() ?? string.Empty;

            fragments.Add($"{{{{ $parameter.{camelProp} }}}} == '{valueStr}'");
        }

        return new DisplayRule
        {
            Condition = string.Join(" || ", fragments),
            Dependencies = dependencies.ToList()
        };
    }
}
