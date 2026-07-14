using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Tools;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// Agent 节点共享的工具描述解析帮助类。
/// </summary>
internal static class AgentToolDescriptionHelper
{
    /// <summary>
    /// 解析工具描述，优先使用参数中的 AI 参数占位符描述，回退到节点 DisplayName。
    /// </summary>
    public static string ResolveToolDescription(INodeType nodeType, NodeTypeDescriptor? descriptor)
    {
        var description = nodeType.DisplayName;
        if (descriptor?.Parameters is { Count: > 0 })
        {
            var aiParamParam = descriptor.Parameters.FirstOrDefault(p =>
                SchemaDerivation.HasAiParamPlaceholder(p.Description));
            if (aiParamParam?.Description is not null)
            {
                description = SchemaDerivation.ResolveAiParamDescription(aiParamParam.Description)
                    ?? description;
            }
        }

        return description;
    }
}
