using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// 透传输入数据到输出的测试节点。
/// </summary>
public sealed class PassThroughNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "passThrough";

    /// <inheritdoc />
    public string DisplayName => "Pass Through";

    /// <inheritdoc />
    public string Category => "Test";

    /// <inheritdoc />
    public string Icon => "test";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var batch = context.Inputs.TryGetValue("input", out var input)
            ? input
            : new DataBatch();

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = batch
        });
    }
}

/// <summary>
/// 对输入数值加 1 的测试节点。
/// </summary>
public sealed class IncrementNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "increment";

    /// <inheritdoc />
    public string DisplayName => "Increment";

    /// <inheritdoc />
    public string Category => "Test";

    /// <inheritdoc />
    public string Icon => "test";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var input = context.Inputs.TryGetValue("input", out var batch) && batch.Items.Count > 0
            ? batch.Items[0].Data
            : null;

        var value = input is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var number)
            ? number + 1
            : 0;

        var result = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = value,
                    Success = true,
                    SourceIndex = 0
                }
            ]
        };

        return Task.FromResult(new NodeExecutionResult { Success = true, Output = result });
    }
}

/// <summary>
/// 根据阈值分支的测试节点。
/// </summary>
public sealed class BranchNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "branch";

    /// <inheritdoc />
    public string DisplayName => "Branch";

    /// <inheritdoc />
    public string Category => "Test";

    /// <inheritdoc />
    public string Icon => "test";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 分支阈值。
    /// </summary>
    [Description("Threshold for branching.")]
    public int Threshold { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "true", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = "false", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var input = context.Inputs.TryGetValue("input", out var batch) && batch.Items.Count > 0
            ? batch.Items[0].Data
            : null;

        var value = input is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var number)
            ? number
            : 0;

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = value,
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            },
            BranchIndex = value > Threshold ? 0 : 1
        });
    }
}

/// <summary>
/// 合并两个输入端口的测试节点。
/// </summary>
public sealed class MergeNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "merge";

    /// <inheritdoc />
    public string DisplayName => "Merge";

    /// <inheritdoc />
    public string Category => "Test";

    /// <inheritdoc />
    public string Icon => "test";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "a", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "b", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var items = new List<DataItem>();

        if (context.Inputs.TryGetValue("a", out var batchA))
        {
            items.AddRange(batchA.Items);
        }

        if (context.Inputs.TryGetValue("b", out var batchB))
        {
            items.AddRange(batchB.Items);
        }

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = items }
        });
    }
}

/// <summary>
/// 始终失败的测试节点。
/// </summary>
public sealed class FailingNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "failing";

    /// <inheritdoc />
    public string DisplayName => "Failing";

    /// <inheritdoc />
    public string Category => "Test";

    /// <inheritdoc />
    public string Icon => "test";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => true;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new NodeExecutionResult
        {
            Success = false,
            Error = new NodeError
            {
                Code = "TestFailure",
                Message = "测试失败。",
                NodeDefinitionId = context.Node.Id
            }
        });
    }
}

/// <summary>
/// 前 N 次调用失败、之后成功的测试节点。
/// </summary>
public sealed class RetryableNode : INodeType
{
    private readonly ConcurrentDictionary<Guid, int> _remainingFailuresByExecution = new();

    /// <inheritdoc />
    public string TypeName => "retryable";

    /// <inheritdoc />
    public string DisplayName => "Retryable";

    /// <inheritdoc />
    public string Category => "Test";

    /// <inheritdoc />
    public string Icon => "test";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 前 N 次执行失败。
    /// </summary>
    [Description("Number of failures before succeeding.")]
    public int FailCount { get; set; } = 2;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => true;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var remaining = _remainingFailuresByExecution.GetOrAdd(context.ExecutionId, FailCount);

        if (remaining > 0)
        {
            _remainingFailuresByExecution[context.ExecutionId] = remaining - 1;
            return Task.FromResult(new NodeExecutionResult
            {
                Success = false,
                Error = new NodeError
                {
                    Code = "RetryFailure",
                    Message = "重试中失败。",
                    NodeDefinitionId = context.Node.Id
                }
            });
        }

        _remainingFailuresByExecution.TryRemove(context.ExecutionId, out _);
        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = "success",
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            }
        });
    }
}

/// <summary>
/// 按输入批次中项目数量分别执行的测试节点。
/// </summary>
public sealed class OncePerItemNode : INodeType
{
    private readonly ConcurrentDictionary<Guid, List<int>> _runIndicesByExecution = new();

    /// <inheritdoc />
    public string TypeName => "oncePerItem";

    /// <inheritdoc />
    public string DisplayName => "Once Per Item";

    /// <inheritdoc />
    public string Category => "Test";

    /// <inheritdoc />
    public string Icon => "test";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OncePerItem;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => true;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var indices = _runIndicesByExecution.GetOrAdd(context.ExecutionId, _ => []);
        lock (indices)
        {
            indices.Add(context.RunIndex);
        }

        var result = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = context.RunIndex,
                    Success = true,
                    SourceIndex = context.RunIndex
                }
            ]
        };

        return Task.FromResult(new NodeExecutionResult { Success = true, Output = result });
    }
}

/// <summary>
/// 长时间运行、可取消的测试节点。
/// </summary>
public sealed class SlowNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "slow";

    /// <inheritdoc />
    public string DisplayName => "Slow";

    /// <inheritdoc />
    public string Category => "Test";

    /// <inheritdoc />
    public string Icon => "test";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => true;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new NodeExecutionResult
            {
                Success = false,
                Error = new NodeError
                {
                    Code = "Cancelled",
                    Message = "节点被取消。",
                    NodeDefinitionId = context.Node.Id
                }
            };
        }

        return new NodeExecutionResult { Success = true, Output = new DataBatch() };
    }
}
