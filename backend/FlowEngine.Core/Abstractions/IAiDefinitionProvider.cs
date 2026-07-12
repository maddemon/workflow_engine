using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// AI 节点定义提供者，允许节点类型覆盖适配器自动推导的 AI-native 定义。
/// </summary>
public interface IAiDefinitionProvider
{
    /// <summary>
    /// 返回该节点类型的 AI-native 定义，覆盖适配器自动推导。
    /// </summary>
    /// <param name="descriptor">由 ParameterDiscoverer 生成的节点描述，供参考。</param>
    AiNodeDefinition GetAiDefinition(NodeTypeDescriptor descriptor);
}
