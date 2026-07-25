using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 记忆节点，用于在工作流执行内读写跨节点共享数据。
/// </summary>
public sealed class MemoryNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "memory";

    /// <inheritdoc />
    public string DisplayName => "Memory";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "database";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

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
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return Task.FromResult(context.ErrorResult("MissingKey", "Memory key is required."));
        }

        return Task.FromResult(Action switch
        {
            MemoryAction.Read => Read(context),
            MemoryAction.Write => Write(context),
            MemoryAction.Clear => Clear(context),
            _ => context.ErrorResult("InvalidAction", $"Unsupported memory action: {Action}")
        });
    }

    private NodeExecutionResult Read(NodeExecutionContext context)
    {
        if (!context.Memory.TryGetValue(Key, out var value))
        {
            return context.ErrorResult("KeyNotFound", $"Memory key '{Key}' not found.");
        }

        return context.CreateSingleResult(value);
    }

    private NodeExecutionResult Write(NodeExecutionContext context)
    {
        var valueToStore = ResolveValue(context);
        context.Memory[Key] = valueToStore;
        return context.CreateSingleResult(valueToStore);
    }

    private NodeExecutionResult Clear(NodeExecutionContext context)
    {
        context.Memory.Remove(Key);
        return context.CreateSingleResult(JsonValue.Create(true));
    }

    private JsonNode? ResolveValue(NodeExecutionContext context)
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

        return context.GetInputPayload();
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
