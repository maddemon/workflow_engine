using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 反射扫描节点属性，生成 <see cref="ParameterDefinition"/> 列表。
/// </summary>
public sealed class ParameterDiscoverer
{
    private readonly ConcurrentDictionary<Type, IReadOnlyList<ParameterDefinition>> _cache = new();

    /// <summary>
    /// 发现指定节点类型的所有参数定义。
    /// </summary>
    public IReadOnlyList<ParameterDefinition> Discover(Type nodeType)
    {
        ArgumentNullException.ThrowIfNull(nodeType);

        return _cache.GetOrAdd(nodeType, DiscoverInternal);
    }

    private static IReadOnlyList<ParameterDefinition> DiscoverInternal(Type nodeType)
    {
        object? instance = null;
        try
        {
            if (!nodeType.IsAbstract)
            {
                instance = Activator.CreateInstance(nodeType);
            }
        }
        catch
        {
            // 无法创建实例，跳过默认值读取
        }

        var parameters = new List<ParameterDefinition>();

        foreach (var property in nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (ShouldSkip(property))
            {
                continue;
            }

            var camelName = ToCamelCase(property.Name);
            var displayName = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                ?? property.Name;

            var hintAttr = property.GetCustomAttribute<HintAttribute>();
            var (parameterType, inferredHint) = InferParameterType(property.PropertyType, hintAttr);

            var credentialAttr = property.GetCustomAttribute<CredentialAttribute>();
            if (credentialAttr is not null)
            {
                parameterType = ParameterType.Credential;
                inferredHint = PresentationHint.CredentialSelect;
            }

            var definition = new ParameterDefinition
            {
                Name = camelName,
                DisplayName = displayName,
                Type = parameterType,
                Required = IsRequired(property.PropertyType),
                DefaultValue = instance is not null ? ReadPropertyDefault(instance, property) : null,
                Hint = hintAttr?.Hint ?? inferredHint,
                Description = property.GetCustomAttribute<DescriptionAttribute>()?.Description,
                CredentialType = credentialAttr?.CredentialType
            };

            if (property.PropertyType.IsEnum)
            {
                definition.Options = BuildEnumOptions(property.PropertyType);
            }

            var optionsProviderAttr = property.GetCustomAttribute<OptionsProviderAttribute>();
            if (optionsProviderAttr is not null && instance is not null)
            {
                var method = nodeType.GetMethod(
                    optionsProviderAttr.MethodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    []);
                if (method is not null)
                {
                    var result = method.Invoke(instance, null);
                    if (result is IEnumerable<Option> options)
                    {
                        definition.Options = options.ToList();
                    }
                    else if (result is Task<IEnumerable<Option>> taskOptions)
                    {
                        definition.Options = taskOptions.GetAwaiter().GetResult().ToList();
                    }
                }
            }

            var conditionAttrs = property.GetCustomAttributes<DisplayConditionAttribute>().ToList();
            if (conditionAttrs.Count > 0)
            {
                definition.DisplayRule = BuildDisplayRule(conditionAttrs);
            }

            var itemAttr = property.GetCustomAttribute<ItemAttribute>();
            if (itemAttr is not null)
            {
                definition.ItemDefinition = BuildItemDefinition(itemAttr.ItemType);
            }

            parameters.Add(definition);
        }

        return parameters;
    }

    private static bool ShouldSkip(PropertyInfo property)
    {
        if (property.GetCustomAttribute<IgnoreParameterAttribute>() is not null)
        {
            return true;
        }

        if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
        {
            return true;
        }

        if (property.Name == nameof(INodeType.Ports))
        {
            return true;
        }

        if (property.DeclaringType == typeof(INodeType))
        {
            return true;
        }

        if (property.GetMethod is null || property.SetMethod is null)
        {
            return true;
        }

        if (property.GetIndexParameters().Length > 0)
        {
            return true;
        }

        if (!IsDeclaredOnNodeType(property))
        {
            return true;
        }

        return false;
    }

    private static bool IsDeclaredOnNodeType(PropertyInfo property)
    {
        var declaringType = property.DeclaringType;
        if (declaringType is null)
        {
            return false;
        }

        if (declaringType.IsInterface)
        {
            return false;
        }

        var interfaceMap = declaringType.GetInterfaceMap(typeof(INodeType));
        foreach (var interfaceMethod in interfaceMap.InterfaceMethods)
        {
            if (property.GetMethod == interfaceMap.TargetMethods[Array.IndexOf(interfaceMap.InterfaceMethods, interfaceMethod)])
            {
                return false;
            }
        }

        return true;
    }

    private static object? ReadPropertyDefault(object instance, PropertyInfo property)
    {
        try
        {
            var value = property.GetValue(instance);
            if (value is string s && s.Length == 0)
            {
                return null;
            }

            if (value is IReadOnlyList<PortDefinition>)
            {
                return null;
            }

            return value;
        }
        catch
        {
            return null;
        }
    }

    private static (ParameterType Type, PresentationHint? Hint) InferParameterType(
        Type clrType, HintAttribute? hintAttr)
    {
        var underlying = Nullable.GetUnderlyingType(clrType);
        var effectiveType = underlying ?? clrType;

        if (effectiveType == typeof(string))
        {
            return hintAttr?.Hint switch
            {
                PresentationHint.CodeEditor => (ParameterType.Code, PresentationHint.CodeEditor),
                _ => (ParameterType.String, null)
            };
        }

        if (effectiveType == typeof(bool))
        {
            return (ParameterType.Boolean, PresentationHint.Toggle);
        }

        if (effectiveType == typeof(int) || effectiveType == typeof(long)
            || effectiveType == typeof(double) || effectiveType == typeof(float))
        {
            return (ParameterType.Number, null);
        }

        if (effectiveType.IsEnum)
        {
            var values = Enum.GetValues(effectiveType);
            var hint = values.Length <= 4
                ? PresentationHint.ButtonGroup
                : PresentationHint.Select;
            return (ParameterType.Options, hint);
        }

        if (typeof(JsonObject).IsAssignableFrom(effectiveType)
            || typeof(JsonNode).IsAssignableFrom(effectiveType))
        {
            return (ParameterType.Json, PresentationHint.JsonEditor);
        }

        if (effectiveType == typeof(Uri) || effectiveType == typeof(System.Net.Mail.MailAddress))
        {
            return (ParameterType.String, null);
        }

        if (effectiveType.IsGenericType
            && effectiveType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            return (ParameterType.Json, PresentationHint.KeyValueEditor);
        }

        if (effectiveType.IsGenericType
            && effectiveType.GetGenericTypeDefinition() == typeof(List<>))
        {
            return (ParameterType.Array, null);
        }

        if (effectiveType.IsArray)
        {
            return (ParameterType.Array, null);
        }

        if (effectiveType == typeof(CredentialValue))
        {
            return (ParameterType.Credential, PresentationHint.CredentialSelect);
        }

        if (effectiveType == typeof(DateTime) || effectiveType == typeof(DateTimeOffset))
        {
            return (ParameterType.String, PresentationHint.DateTime);
        }

        return (ParameterType.Json, null);
    }

    private static bool IsRequired(Type clrType)
    {
        if (!clrType.IsValueType)
        {
            return false;
        }

        return Nullable.GetUnderlyingType(clrType) is null;
    }

    private static List<Option> BuildEnumOptions(Type enumType)
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

    private static DisplayRule BuildDisplayRule(List<DisplayConditionAttribute> conditions)
    {
        var fragments = new List<string>();
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var condition in conditions)
        {
            var camelProp = ToCamelCase(condition.PropertyName);
            dependencies.Add(camelProp);
            fragments.Add($"{{{{ $parameter.{camelProp} }}}} == '{condition.Value}'");
        }

        return new DisplayRule
        {
            Condition = string.Join(" || ", fragments),
            Dependencies = dependencies.ToList()
        };
    }

    private static ParameterDefinition BuildItemDefinition(Type itemType)
    {
        var fieldDefs = new List<ParameterDefinition>();

        foreach (var prop in itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetMethod is null || prop.SetMethod is null)
            {
                continue;
            }

            var hintAttr = prop.GetCustomAttribute<HintAttribute>();
            var (paramType, inferredHint) = InferParameterType(prop.PropertyType, hintAttr);
            var displayName = prop.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                ?? prop.Name;

            fieldDefs.Add(new ParameterDefinition
            {
                Name = ToCamelCase(prop.Name),
                DisplayName = displayName,
                Type = paramType,
                Hint = hintAttr?.Hint ?? inferredHint,
                Description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description,
                Required = IsRequired(prop.PropertyType)
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

    internal static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
