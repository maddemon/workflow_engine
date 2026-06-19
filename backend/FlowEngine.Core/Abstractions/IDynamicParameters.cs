using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 可选接口，节点实现此接口可在运行时动态补充参数定义。
/// 适用于参数名称/数量/类型编译时未知、由运行时配置决定的场景（如子流程节点）。
/// </summary>
public interface IDynamicParameters
{
    /// <summary>
    /// 根据已解析的参数值，返回运行时动态参数定义。
    /// 返回结果与静态属性参数合并（同名以静态为准）。
    /// </summary>
    /// <param name="resolvedValues">已解析的静态参数值（camelCase 键）。</param>
    /// <returns>动态参数定义列表。</returns>
    IReadOnlyList<ParameterDefinition> GetDynamicParameters(
        IReadOnlyDictionary<string, object> resolvedValues);
}
