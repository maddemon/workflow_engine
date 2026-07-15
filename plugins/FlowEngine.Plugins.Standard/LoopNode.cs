using FlowEngine.Core;
using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 循环节点，将数据分批处理。
/// </summary>
public sealed class LoopNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "loop";

    /// <inheritdoc />
    public string DisplayName => "Loop Over Items";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "repeat";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 批次大小。
    /// </summary>
    [Description("Number of items to process in each batch.")]
    public int BatchSize { get; set; } = 1;

    /// <summary>
    /// 是否重置索引。
    /// </summary>
    [Description("Whether to reset the item index at the start of each batch.")]
    public bool ResetIndex { get; set; } = false;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Loop, DisplayName = "Loop", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Done, DisplayName = "Done", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        BatchSize = Math.Max(1, BatchSize);

        var inputBatch = context.GetInputBatch();

        // Check if this is a "next batch" call from downstream
        if (IsNextBatchCall(context))
        {
            return HandleNextBatch(context, inputBatch);
        }

        // Initial call - return first batch on loop output
        return HandleInitialBatch(context, inputBatch);
    }

    private bool IsNextBatchCall(NodeExecutionContext context)
    {
        // Check if there's a "nextBatch" parameter indicating this is a subsequent call
        return context.ResolvedParameters.TryGetValue("nextBatch", out var nextBatch) &&
               nextBatch is JsonValue boolVal &&
               boolVal.TryGetValue<bool>(out var isNext) &&
               isNext;
    }

    private Task<NodeExecutionResult> HandleNextBatch(NodeExecutionContext context, DataBatch inputBatch)
    {
        // Get current position from context
        var position = 0;
        if (context.ResolvedParameters.TryGetValue("position", out var posVal) &&
            posVal is JsonValue posJson &&
            posJson.TryGetValue<int>(out var pos))
        {
            position = pos;
        }

        var allItems = inputBatch.Items;
        var totalItems = allItems.Count;

        if (position >= totalItems)
        {
            // No more items, return done
            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch(),
                BranchIndex = 1 // done
            });
        }

        // Return next batch on loop output
        var batchItems = allItems.Skip(position).Take(BatchSize).ToList();
        var newPosition = position + batchItems.Count;

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = batchItems },
            BranchIndex = 0 // loop
        });
    }

    private Task<NodeExecutionResult> HandleInitialBatch(NodeExecutionContext context, DataBatch inputBatch)
    {
        var allItems = inputBatch.Items;
        var totalItems = allItems.Count;

        if (totalItems == 0)
        {
            // No items, return done immediately
            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch(),
                BranchIndex = 1 // done
            });
        }

        // Return first batch on loop output
        var batchItems = allItems.Take(BatchSize).ToList();

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = batchItems },
            BranchIndex = 0 // loop
        });
    }
}
