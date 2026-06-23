using System.ComponentModel;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 合并节点，将多个输入分支的数据合并为一个输出。
/// </summary>
public sealed class MergeNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "merge";

    /// <inheritdoc />
    public string DisplayName => "Merge";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "merge";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 合并模式。
    /// </summary>
    [Description("How to merge the input data.")]
    public MergeMode Mode { get; set; } = MergeMode.Append;

    /// <summary>
    /// 合并操作（Append 模式下可用）。
    /// </summary>
    [Description("How to combine items in Combine mode.")]
    public CombineOperation CombineOperation { get; set; } = CombineOperation.CombineByPosition;

    /// <summary>
    /// 用于匹配的字段名（CombineByField 模式下使用）。
    /// </summary>
    [Description("Field name to use for matching items in CombineByField mode.")]
    public string MatchField { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input1", DisplayName = "Input 1", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "input2", DisplayName = "Input 2", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var batch1 = context.Inputs.TryGetValue("input1", out var b1) ? b1 : new DataBatch();
        var batch2 = context.Inputs.TryGetValue("input2", out var b2) ? b2 : new DataBatch();

        var result = Mode switch
        {
            MergeMode.Append => MergeAppend(batch1, batch2),
            MergeMode.Combine => MergeCombine(batch1, batch2),
            MergeMode.Multiplex => MergeMultiplex(batch1, batch2),
            _ => MergeAppend(batch1, batch2)
        };

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = result
        });
    }

    private DataBatch MergeAppend(DataBatch batch1, DataBatch batch2)
    {
        var items = new List<DataItem>();
        items.AddRange(batch1.Items);
        items.AddRange(batch2.Items);
        return new DataBatch { Items = items };
    }

    private DataBatch MergeCombine(DataBatch batch1, DataBatch batch2)
    {
        return CombineOperation switch
        {
            CombineOperation.CombineByPosition => CombineByPosition(batch1, batch2),
            CombineOperation.CombineByField => CombineByField(batch1, batch2),
            CombineOperation.MergeByPosition => MergeByPosition(batch1, batch2),
            _ => CombineByPosition(batch1, batch2)
        };
    }

    private DataBatch CombineByPosition(DataBatch batch1, DataBatch batch2)
    {
        var items = new List<DataItem>();
        var maxCount = Math.Max(batch1.Items.Count, batch2.Items.Count);

        for (var i = 0; i < maxCount; i++)
        {
            var item1 = i < batch1.Items.Count ? batch1.Items[i] : null;
            var item2 = i < batch2.Items.Count ? batch2.Items[i] : null;

            var merged = MergeJsonNodes(item1?.Data, item2?.Data);
            items.Add(new DataItem
            {
                Data = merged,
                Success = true,
                SourceIndex = i
            });
        }

        return new DataBatch { Items = items };
    }

    private DataBatch CombineByField(DataBatch batch1, DataBatch batch2)
    {
        if (string.IsNullOrEmpty(MatchField))
        {
            return MergeAppend(batch1, batch2);
        }

        var lookup = batch2.Items.ToDictionary(
            item => GetFieldValue(item.Data, MatchField) ?? string.Empty,
            item => item);

        var items = new List<DataItem>();

        foreach (var item1 in batch1.Items)
        {
            var key = GetFieldValue(item1.Data, MatchField) ?? string.Empty;
            if (lookup.TryGetValue(key, out var item2))
            {
                var merged = MergeJsonNodes(item1.Data, item2.Data);
                items.Add(new DataItem
                {
                    Data = merged,
                    Success = true,
                    SourceIndex = item1.SourceIndex
                });
            }
            else
            {
                items.Add(item1);
            }
        }

        return new DataBatch { Items = items };
    }

    private DataBatch MergeByPosition(DataBatch batch1, DataBatch batch2)
    {
        var items = new List<DataItem>();
        var maxCount = Math.Max(batch1.Items.Count, batch2.Items.Count);

        for (var i = 0; i < maxCount; i++)
        {
            var item1 = i < batch1.Items.Count ? batch1.Items[i] : null;
            var item2 = i < batch2.Items.Count ? batch2.Items[i] : null;

            // Use item1 if available, otherwise item2
            var result = item1 ?? item2;
            if (result is not null)
            {
                items.Add(result);
            }
        }

        return new DataBatch { Items = items };
    }

    private DataBatch MergeMultiplex(DataBatch batch1, DataBatch batch2)
    {
        var items = new List<DataItem>();

        foreach (var item1 in batch1.Items)
        {
            foreach (var item2 in batch2.Items)
            {
                var merged = MergeJsonNodes(item1.Data, item2.Data);
                items.Add(new DataItem
                {
                    Data = merged,
                    Success = true,
                    SourceIndex = items.Count
                });
            }
        }

        return new DataBatch { Items = items };
    }

    private static System.Text.Json.Nodes.JsonNode? MergeJsonNodes(
        System.Text.Json.Nodes.JsonNode? node1,
        System.Text.Json.Nodes.JsonNode? node2)
    {
        if (node1 is null) return node2;
        if (node2 is null) return node1;

        if (node1 is System.Text.Json.Nodes.JsonObject obj1 && node2 is System.Text.Json.Nodes.JsonObject obj2)
        {
            var merged = obj1.DeepClone().AsObject();
            foreach (var prop in obj2)
            {
                merged[prop.Key] = prop.Value?.DeepClone();
            }
            return merged;
        }

        // For non-object types, prefer node1
        return node1;
    }

    private static string? GetFieldValue(System.Text.Json.Nodes.JsonNode? data, string fieldPath)
    {
        if (data is null || string.IsNullOrEmpty(fieldPath))
        {
            return null;
        }

        if (data is not System.Text.Json.Nodes.JsonObject obj)
        {
            return null;
        }

        var parts = fieldPath.Split('.');
        System.Text.Json.Nodes.JsonNode? current = obj;

        foreach (var part in parts)
        {
            if (current is System.Text.Json.Nodes.JsonObject currentObj &&
                currentObj.TryGetPropertyValue(part, out var next))
            {
                current = next;
            }
            else
            {
                return null;
            }
        }

        return current?.ToString();
    }
}

/// <summary>
/// 合并模式。
/// </summary>
public enum MergeMode
{
    /// <summary>追加所有项目</summary>
    Append,

    /// <summary>组合项目</summary>
    Combine,

    /// <summary>交叉组合</summary>
    Multiplex
}

/// <summary>
/// 组合操作类型。
/// </summary>
public enum CombineOperation
{
    /// <summary>按位置组合</summary>
    CombineByPosition,

    /// <summary>按字段匹配组合</summary>
    CombineByField,

    /// <summary>按位置合并（优先使用第一个输入）</summary>
    MergeByPosition
}
