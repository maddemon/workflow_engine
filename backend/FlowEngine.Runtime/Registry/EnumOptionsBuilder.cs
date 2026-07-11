using System.ComponentModel;
using System.Reflection;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 从枚举类型构建选项列表。
/// </summary>
internal static class EnumOptionsBuilder
{
    public static List<Option> Build(Type enumType)
    {
        var options = new List<Option>();
        foreach (var value in Enum.GetValues(enumType))
        {
            var field = enumType.GetField(value.ToString()!);
            var label = field?.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? value.ToString()!;
            options.Add(new Option { Label = label, Value = value.ToString()! });
        }

        return options;
    }
}
