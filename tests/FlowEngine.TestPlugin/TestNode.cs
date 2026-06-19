using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.TestPlugin;

/// <summary>
/// 测试用节点类型。
/// </summary>
public sealed class TestNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "testNode";

    /// <inheritdoc />
    public string DisplayName => "Test Node";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "test-icon";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } = [];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new NodeExecutionResult { Success = true });
    }
}
