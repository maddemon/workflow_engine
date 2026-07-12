using System.Text.Json.Nodes;
using FlowEngine.Core.Ai;

namespace FlowEngine.Core.Ai;

/// <summary>
/// AI 节点定义的构造辅助。
/// 解决自动推导描述无意义（如「Sort 节点」）、示例/输出结构缺失的问题（task-013 P4）：
/// 各标准节点在 <see cref="INodeType.GetAiDefinition"/> 的 override 中调用本类辅助方法，
/// 手写可读的 description / tags / examples / outputSchema；输入 schema 与端口统一回退到
/// <see cref="NodeDefinitionAdapter"/> 自动推导（结构已正确），此处只补充语义层信息。
/// Name 由适配器统一取节点 TypeName，这里不设置。
/// </summary>
public static class AiDefinitionHelpers
{
    /// <summary>
    /// 构造 AI-native 节点定义。
    /// </summary>
    /// <param name="displayName">显示名称。</param>
    /// <param name="category">节点类别。</param>
    /// <param name="isTrigger">是否为触发器（以节点类别是否为 Trigger 为准的覆盖值）。</param>
    /// <param name="description">可读的节点用途描述。</param>
    /// <param name="tags">标签，用于 AI 检索。</param>
    /// <param name="outputSchema">输出结构（可选，缺省回退自动推导）。</param>
    /// <param name="examples">示例（可选）。</param>
    public static AiNodeDefinition Def(
        string displayName,
        string category,
        bool isTrigger,
        string description,
        string[] tags,
        JsonNode? outputSchema = null,
        params AiExample[] examples) =>
        new()
        {
            DisplayName = displayName,
            Category = category,
            IsTrigger = isTrigger,
            Description = description,
            Tags = [.. tags],
            OutputSchema = outputSchema,
            Examples = [.. examples],
        };

    /// <summary>
    /// 构造 AI 定义示例。
    /// </summary>
    /// <param name="description">示例说明。</param>
    /// <param name="input">示例输入。</param>
    /// <param name="output">示例输出。</param>
    public static AiExample Example(string description, JsonNode? input, JsonNode? output) =>
        new() { Description = description, Input = input, Output = output };
}
