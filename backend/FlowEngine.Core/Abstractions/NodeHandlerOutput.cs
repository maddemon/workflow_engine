using System.Collections.Generic;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>节点业务输出（纯数据）。框架负责包装为 <see cref="NodeExecutionResult"/>。
/// 命名为 NodeHandlerOutput 以区别于 FlowEngine.Core.Scripting.NodeOutput（表达式可见的节点输出）。
/// PortOutputs 用于多输出端口路由；ContextChanges 暂保留可选，框架当前忽略。</summary>
public sealed class NodeHandlerOutput
{
    /// <summary>主输出数据批次。</summary>
    public DataBatch Batch { get; }

    /// <summary>多输出端口映射（端口名 -&gt; 批次），用于分支/多端口路由。</summary>
    public IReadOnlyDictionary<string, DataBatch>? PortOutputs { get; }

    /// <summary>节点级上下文变更（可选），框架当前忽略。</summary>
    public IReadOnlyDictionary<string, object?>? ContextChanges { get; }

    /// <summary>业务失败错误（可选）。设置后框架将结果标记为失败，但仍携带 <see cref="Batch"/> 输出（如 Agent 失败仍输出结果 DTO）。</summary>
    public NodeError? Error { get; init; }

    private NodeHandlerOutput(DataBatch batch, IReadOnlyDictionary<string, DataBatch>? portOutputs, IReadOnlyDictionary<string, object?>? contextChanges)
        => (Batch, PortOutputs, ContextChanges) = (batch, portOutputs, contextChanges);

    /// <summary>创建单一主输出（无端口分发）。</summary>
    /// <param name="batch">输出批次。</param>
    public static NodeHandlerOutput Data(DataBatch batch) => new(batch, null, null);

    /// <summary>创建失败输出：标记业务错误，但仍可携带输出批次（如 Agent 失败时的结果 DTO）。</summary>
    /// <param name="code">错误码。</param>
    /// <param name="message">错误描述。</param>
    /// <param name="output">失败情况下仍携带的输出批次（可空，缺省为空批次）。</param>
    /// <param name="nodeDefinitionId">节点定义 ID（错误定位用）。</param>
    public static NodeHandlerOutput Failure(string code, string message, DataBatch? output = null, string? nodeDefinitionId = "")
        => new(output ?? new DataBatch(), null, null)
        {
            Error = new NodeError { Code = code, Message = message, NodeDefinitionId = nodeDefinitionId ?? string.Empty }
        };

    /// <summary>创建单端口输出：批次同时作为主输出与该命名端口输出。</summary>
    /// <param name="portName">端口名称。</param>
    /// <param name="batch">输出批次。</param>
    public static NodeHandlerOutput ToPort(string portName, DataBatch batch) => new(new DataBatch(), new Dictionary<string, DataBatch> { [portName] = batch }, null);

    /// <summary>创建多端口输出：各端口分别输出对应批次，主输出取首个端口批次。</summary>
    /// <param name="portOutputs">端口名 -&gt; 批次 映射。</param>
    public static NodeHandlerOutput ToPorts(IReadOnlyDictionary<string, DataBatch> portOutputs) => new(new DataBatch(), portOutputs, null);
}
