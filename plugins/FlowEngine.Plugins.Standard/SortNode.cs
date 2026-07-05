using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 排序节点，对数据进行排序。
/// </summary>
public sealed class SortNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "sort";

    /// <inheritdoc />
    public string DisplayName => "Sort";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "sort";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 排序字段列表。
    /// </summary>
    [Description("Fields to sort by.")]
    public List<SortField> SortFields { get; set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputBatch = context.Inputs.TryGetValue("input", out var batch)
            ? batch
            : new DataBatch();

        if (SortFields.Count == 0)
        {
            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = inputBatch
            });
        }

        IOrderedEnumerable<DataItem> sortedItems;

        if (SortFields[0].Direction == SortDirection.Asc)
        {
            sortedItems = inputBatch.Items
                .OrderBy(item => GetSortKey(item.Data, SortFields[0]));
        }
        else
        {
            sortedItems = inputBatch.Items
                .OrderByDescending(item => GetSortKey(item.Data, SortFields[0]));
        }

        // Apply secondary sort fields if any
        foreach (var field in SortFields.Skip(1))
        {
            if (field.Direction == SortDirection.Asc)
            {
                sortedItems = sortedItems.ThenBy(item => GetSortKey(item.Data, field));
            }
            else
            {
                sortedItems = sortedItems.ThenByDescending(item => GetSortKey(item.Data, field));
            }
        }

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = sortedItems.ToList() }
        });
    }

    private IComparable GetSortKey(JsonNode? data, SortField field)
    {
        var value = GetFieldValue(data, field.FieldName);

        if (value is null)
        {
            return string.Empty;
        }

        // Try to parse as number for numeric sorting
        if (double.TryParse(value, out var number))
        {
            return number;
        }

        // Try to parse as date for date sorting
        if (DateTime.TryParse(value, out var date))
        {
            return date;
        }

        // Default to string comparison
        return value;
    }

    private static string? GetFieldValue(JsonNode? data, string fieldPath)
    {
        if (data is null || string.IsNullOrEmpty(fieldPath))
        {
            return null;
        }

        if (data is not JsonObject obj)
        {
            return null;
        }

        var parts = fieldPath.Split('.');
        JsonNode? current = obj;

        foreach (var part in parts)
        {
            if (current is JsonObject currentObj && currentObj.TryGetPropertyValue(part, out var next))
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
/// 排序字段定义。
/// </summary>
public sealed class SortField
{
    /// <summary>
    /// 字段名称。
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// 排序方向。
    /// </summary>
    public SortDirection Direction { get; set; } = SortDirection.Asc;
}

/// <summary>
/// 排序方向。
/// </summary>
public enum SortDirection
{
    Asc,
    Desc
}
