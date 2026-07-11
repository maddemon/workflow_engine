using System.Reflection;
using System.Text.Json.Serialization;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 过滤节点属性，排除非参数属性。
/// </summary>
internal static class PropertyFilter
{
    public static bool ShouldSkip(PropertyInfo property)
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

        // 声明类型未实现 INodeType 时（例如仅叶子类实现接口、属性声明在未实现接口的基类上），
        // 调用 GetInterfaceMap 会抛异常。该属性不可能是 INodeType 接口成员，直接保留为参数属性。
        if (!typeof(INodeType).IsAssignableFrom(declaringType))
        {
            return true;
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
}
