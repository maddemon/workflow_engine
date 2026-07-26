using FlowEngine.Core.Agent;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>工具解析器抽象：供 AgentNode 等节点经接口解析 LLM 工具调用（工具定义 → 节点定义 → 节点类型实例），
/// 取代经 <see cref="NodeExecutionContext"/> 依赖 <see cref="INodeRegistry"/> 的查找方式，便于 Phase 4 节点迁移。
/// 实现位于 <see cref="FlowEngine.Core.Agent.ToolResolver"/>（内部类），节点只依赖本接口。</summary>
public interface IToolResolver
{
    /// <summary>解析工具调用所需的三级查找：工具定义 → 节点定义 → 节点类型。</summary>
    /// <param name="toolCall">LLM 返回的工具调用。</param>
    /// <returns>解析结果（含可能的错误信息）。</returns>
    ToolResolution Resolve(LlmToolCall toolCall);
}
