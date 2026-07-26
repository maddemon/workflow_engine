using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 记忆节点，用于在工作流执行内读写跨节点共享数据。
/// </summary>
[NodeMeta(TypeName = "memory", DisplayName = "Memory", Category = NodeCategory.AI, Icon = "database", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class MemoryNode : NodeBase
{
    /// <summary>
    /// 记忆操作类型。
    /// </summary>
    [Description("Memory operation: read, write, or clear.")]
    public MemoryAction Action { get; set; } = MemoryAction.Read;

    /// <summary>
    /// 记忆键名。
    /// </summary>
    [Description("Memory key.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 要写入的值（仅 write 操作使用）。支持 JSON 字面量；为空时取输入端口数据。
    /// </summary>
    [Description("Value to write (used by write action). Supports JSON literals; falls back to input data when empty.")]
    public string? Value { get; set; }

    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new NodeExecutionException("MissingKey", "Memory key is required.");
        }

        var result = Action switch
        {
            MemoryAction.Read => Read(),
            MemoryAction.Write => Write(input),
            MemoryAction.Clear => Clear(),
            _ => throw new NodeExecutionException("InvalidAction", $"Unsupported memory action: {Action}")
        };

        return Task.FromResult(result);
    }

    private NodeHandlerOutput Read()
    {
        if (!ExecutionContext.Memory.TryGetValue(Key, out var value))
        {
            throw new NodeExecutionException("KeyNotFound", $"Memory key '{Key}' not found.");
        }

        return NodeHandlerOutput.Data(SingleItemBatch(value));
    }

    private NodeHandlerOutput Write(NodeInput input)
    {
        var valueToStore = ResolveValue(input);
        ExecutionContext.Memory[Key] = valueToStore;
        return NodeHandlerOutput.Data(SingleItemBatch(valueToStore));
    }

    private NodeHandlerOutput Clear()
    {
        ExecutionContext.Memory.Remove(Key);
        return NodeHandlerOutput.Data(SingleItemBatch(JsonValue.Create(true)));
    }

    private static DataBatch SingleItemBatch(JsonNode? data) => new()
    {
        Items =
        [
            new DataItem
            {
                Data = data,
                Success = true,
                SourceIndex = 0
            }
        ]
    };

    private JsonNode? ResolveValue(NodeInput input)
    {
        if (!string.IsNullOrWhiteSpace(Value))
        {
            try
            {
                return JsonNode.Parse(Value);
            }
            catch
            {
                return JsonValue.Create(Value);
            }
        }

        var batch = input.InputBatch;
        return batch.Items.Count > 0 ? batch.Items[0].Data : null;
    }
}

/// <summary>
/// 记忆节点操作类型。
/// </summary>
public enum MemoryAction
{
    /// <summary>读取</summary>
    Read,

    /// <summary>写入</summary>
    Write,

    /// <summary>清除</summary>
    Clear
}
