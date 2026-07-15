using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 反射扫描节点属性，生成 <see cref="ParameterDefinition"/> 列表。
/// </summary>
/// <remarks>
/// 初始化 ParameterDiscoverer。
/// </remarks>
/// <param name="logger">日志记录器（可选）。</param>
public sealed class ParameterDiscoverer(ILogger? logger = null)
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

    /// <summary>
    /// 异步发现指定节点类型的所有参数定义（支持异步选项提供者）。
    /// </summary>
    public async Task<IReadOnlyList<ParameterDefinition>> DiscoverAsync(Type nodeType)
    {
        ArgumentNullException.ThrowIfNull(nodeType);

        if (_cache.TryGetValue(nodeType, out var cached))
        {
            return cached;
        }

        var result = await DiscoverInternalAsync(nodeType).ConfigureAwait(false);
        _cache.TryAdd(nodeType, result);
        return result;
    }

    private IReadOnlyList<ParameterDefinition> DiscoverInternal(Type nodeType)
    {
        object? instance = null;
        try
        {
            if (!nodeType.IsAbstract)
            {
                instance = Activator.CreateInstance(nodeType);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "无法创建节点类型 {NodeType} 的实例，跳过默认值读取。", nodeType.Name);
        }

        var parameters = new List<ParameterDefinition>();

        foreach (var property in nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (PropertyFilter.ShouldSkip(property))
            {
                continue;
            }

            var camelName = ToCamelCase(property.Name);
            var displayName = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                ?? property.Name;

            var hintAttr = property.GetCustomAttribute<HintAttribute>();
            var (parameterType, inferredHint) = ParameterTypeInferrer.Infer(property.PropertyType, hintAttr);

            var credentialAttr = property.GetCustomAttribute<CredentialAttribute>();
            if (credentialAttr is not null)
            {
                parameterType = ParameterType.Credential;
                inferredHint = PresentationHint.CredentialSelect;
            }

            // 转换 HintProperties，将 Type 对象转为字符串避免序列化问题
            IReadOnlyDictionary<string, object>? hintProperties = null;
            if (hintAttr?.Properties is { Count: > 0 })
            {
                var converted = new Dictionary<string, object>();
                foreach (var kvp in hintAttr.Properties)
                {
                    converted[kvp.Key] = kvp.Value is Type type
                        ? type.AssemblyQualifiedName ?? type.Name
                        : kvp.Value;
                }
                hintProperties = converted;
            }

            var definition = new ParameterDefinition
            {
                Name = camelName,
                DisplayName = displayName,
                Type = parameterType,
                Required = ParameterTypeInferrer.IsRequired(property.PropertyType),
                DefaultValue = instance is not null ? ReadPropertyDefault(instance, property) : null,
                Hint = hintAttr?.Component ?? inferredHint,
                HintProperties = hintProperties,
                Description = property.GetCustomAttribute<DescriptionAttribute>()?.Description,
                CredentialType = credentialAttr?.CredentialType
            };

            if (property.PropertyType.IsEnum)
            {
                definition.Options = EnumOptionsBuilder.Build(property.PropertyType);
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
                    else if (result is Task<IEnumerable<Option>>)
                    {
                        // 异步选项提供者不支持同步 Discover，请使用 DiscoverAsync。
                        throw new NotSupportedException(
                            $"节点类型 {nodeType.Name} 的属性 {property.Name} 使用了异步选项提供者，" +
                            "请调用 DiscoverAsync 代替 Discover。");
                    }
                }
            }

            var conditionAttrs = property.GetCustomAttributes<DisplayConditionAttribute>().ToList();
            if (conditionAttrs.Count > 0)
            {
                definition.DisplayRule = DisplayRuleBuilder.Build(conditionAttrs);
            }

            // 处理数组子项定义（从 HintProperties 的 itemType 获取）
            if (hintAttr?.Properties is not null &&
                hintAttr.Properties.TryGetValue("itemType", out var itemTypeObj))
            {
                Type? itemType = itemTypeObj switch
                {
                    Type t => t,
                    string typeName => Type.GetType(typeName),
                    _ => null
                };

                if (itemType is not null)
                {
                    definition.ItemDefinition = ArrayItemDefinitionBuilder.BuildItemDefinition(itemType);
                }
            }

            // 未显式指定 itemType 时，对 List<T>/T[] 的复杂类型子项自动推断
            if (definition.Type == ParameterType.Array && definition.ItemDefinition is null)
            {
                var itemType = ArrayItemDefinitionBuilder.GetArrayElementType(property.PropertyType);
                if (itemType is not null && ArrayItemDefinitionBuilder.ShouldBuildItemDefinition(itemType))
                {
                    var itemDef = ArrayItemDefinitionBuilder.BuildItemDefinition(itemType);
                    if (itemDef.Fields.Count > 0)
                    {
                        definition.ItemDefinition = itemDef;
                    }
                }
            }

            parameters.Add(definition);
        }

        return parameters;
    }

    private async Task<IReadOnlyList<ParameterDefinition>> DiscoverInternalAsync(Type nodeType)
    {
        object? instance = null;
        try
        {
            if (!nodeType.IsAbstract)
            {
                instance = Activator.CreateInstance(nodeType);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "无法创建节点类型 {NodeType} 的实例，跳过默认值读取。", nodeType.Name);
        }

        var parameters = new List<ParameterDefinition>();

        foreach (var property in nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (PropertyFilter.ShouldSkip(property))
            {
                continue;
            }

            var camelName = ToCamelCase(property.Name);
            var displayName = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                ?? property.Name;

            var hintAttr = property.GetCustomAttribute<HintAttribute>();
            var (parameterType, inferredHint) = ParameterTypeInferrer.Infer(property.PropertyType, hintAttr);

            var credentialAttr = property.GetCustomAttribute<CredentialAttribute>();
            if (credentialAttr is not null)
            {
                parameterType = ParameterType.Credential;
                inferredHint = PresentationHint.CredentialSelect;
            }

            // 转换 HintProperties，将 Type 对象转为字符串避免序列化问题
            IReadOnlyDictionary<string, object>? hintProperties = null;
            if (hintAttr?.Properties is { Count: > 0 })
            {
                var converted = new Dictionary<string, object>();
                foreach (var kvp in hintAttr.Properties)
                {
                    converted[kvp.Key] = kvp.Value is Type type
                        ? type.AssemblyQualifiedName ?? type.Name
                        : kvp.Value;
                }
                hintProperties = converted;
            }

            var definition = new ParameterDefinition
            {
                Name = camelName,
                DisplayName = displayName,
                Type = parameterType,
                Required = ParameterTypeInferrer.IsRequired(property.PropertyType),
                DefaultValue = instance is not null ? ReadPropertyDefault(instance, property) : null,
                Hint = hintAttr?.Component ?? inferredHint,
                HintProperties = hintProperties,
                Description = property.GetCustomAttribute<DescriptionAttribute>()?.Description,
                CredentialType = credentialAttr?.CredentialType
            };

            if (property.PropertyType.IsEnum)
            {
                definition.Options = EnumOptionsBuilder.Build(property.PropertyType);
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
                        definition.Options = (await taskOptions.ConfigureAwait(false)).ToList();
                    }
                }
            }

            var conditionAttrs = property.GetCustomAttributes<DisplayConditionAttribute>().ToList();
            if (conditionAttrs.Count > 0)
            {
                definition.DisplayRule = DisplayRuleBuilder.Build(conditionAttrs);
            }

            // 处理数组子项定义（从 HintProperties 的 itemType 获取）
            if (hintAttr?.Properties is not null &&
                hintAttr.Properties.TryGetValue("itemType", out var itemTypeObj))
            {
                Type? itemType = itemTypeObj switch
                {
                    Type t => t,
                    string typeName => Type.GetType(typeName),
                    _ => null
                };

                if (itemType is not null)
                {
                    definition.ItemDefinition = ArrayItemDefinitionBuilder.BuildItemDefinition(itemType);
                }
            }

            // 未显式指定 itemType 时，对 List<T>/T[] 的复杂类型子项自动推断
            if (definition.Type == ParameterType.Array && definition.ItemDefinition is null)
            {
                var itemType = ArrayItemDefinitionBuilder.GetArrayElementType(property.PropertyType);
                if (itemType is not null && ArrayItemDefinitionBuilder.ShouldBuildItemDefinition(itemType))
                {
                    var itemDef = ArrayItemDefinitionBuilder.BuildItemDefinition(itemType);
                    if (itemDef.Fields.Count > 0)
                    {
                        definition.ItemDefinition = itemDef;
                    }
                }
            }

            parameters.Add(definition);
        }

        return parameters;
    }

    private object? ReadPropertyDefault(object instance, PropertyInfo property)
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
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "读取属性 {PropertyName} 默认值失败。", property.Name);
            return null;
        }
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
