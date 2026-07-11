using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 构建数组类型的子项参数定义。
/// </summary>
internal static class ArrayItemDefinitionBuilder
{
    public static ParameterDefinition BuildItemDefinition(Type itemType)
    {
        var fieldDefs = new List<ParameterDefinition>();

        foreach (var prop in itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetMethod is null || prop.SetMethod is null)
            {
                continue;
            }

            var hintAttr = prop.GetCustomAttribute<HintAttribute>();
            var (paramType, inferredHint) = ParameterTypeInferrer.Infer(prop.PropertyType, hintAttr);
            var displayName = prop.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                ?? prop.Name;

            fieldDefs.Add(new ParameterDefinition
            {
                Name = ParameterDiscoverer.ToCamelCase(prop.Name),
                DisplayName = displayName,
                Type = paramType,
                Hint = hintAttr?.Component ?? inferredHint,
                Description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description,
                Required = ParameterTypeInferrer.IsRequired(prop.PropertyType)
            });
        }

        return new ParameterDefinition
        {
            Name = "item",
            DisplayName = "Item",
            Type = ParameterType.Json,
            Fields = fieldDefs
        };
    }

    public static Type? GetArrayElementType(Type propertyType)
    {
        var effectiveType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (effectiveType.IsArray)
        {
            return effectiveType.GetElementType();
        }

        if (effectiveType.IsGenericType)
        {
            var genericDef = effectiveType.GetGenericTypeDefinition();
            if (genericDef == typeof(List<>) || genericDef == typeof(IList<>) || genericDef == typeof(IReadOnlyList<>) || genericDef == typeof(ICollection<>) || genericDef == typeof(IEnumerable<>))
            {
                return effectiveType.GetGenericArguments()[0];
            }
        }

        return null;
    }

    public static bool ShouldBuildItemDefinition(Type itemType)
    {
        if (!itemType.IsClass || itemType == typeof(string))
        {
            return false;
        }

        if (itemType.IsAbstract || itemType.IsInterface)
        {
            return false;
        }

        if (typeof(JsonNode).IsAssignableFrom(itemType))
        {
            return false;
        }

        return itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.GetMethod is not null && p.SetMethod is not null);
    }
}
