using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Scripting;
using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 去重节点，移除重复的数据项。
/// </summary>
public sealed class DeduplicateNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "deduplicate";

    /// <inheritdoc />
    public string DisplayName => "Remove Duplicates";

    /// <inheritdoc />
    public string Category => "Data";

    /// <inheritdoc />
    public string Icon => "filter-1";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 用于判断重复的字段名。
    /// </summary>
    [Description("Field name to check for duplicates. Leave empty to check entire item.")]
    public string CompareField { get; set; } = string.Empty;

    /// <summary>
    /// 是否保留第一个匹配项。
    /// </summary>
    [Description("Whether to keep the first occurrence (true) or last occurrence (false).")]
    public bool KeepFirst { get; set; } = true;

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
        var inputBatch = context.GetInputBatch();

        var seen = new HashSet<string>();
        var outputItems = new List<DataItem>();

        if (KeepFirst)
        {
            // Keep first occurrence
            foreach (var item in inputBatch.Items)
            {
                var key = GetItemKey(item.Data);
                if (seen.Add(key))
                {
                    outputItems.Add(item);
                }
            }
        }
        else
        {
            // Keep last occurrence - need to iterate in reverse
            for (var i = inputBatch.Items.Count - 1; i >= 0; i--)
            {
                var item = inputBatch.Items[i];
                var key = GetItemKey(item.Data);
                if (seen.Add(key))
                {
                    outputItems.Add(item);
                }
            }
            outputItems.Reverse();
        }

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = outputItems }
        });
    }

    private string GetItemKey(JsonNode? data)
    {
        if (string.IsNullOrEmpty(CompareField))
        {
            // Use entire item as key
            return data?.ToJsonString() ?? string.Empty;
        }

        // Use specific field as key
        var value = JsonPath.GetValue(data, CompareField);
        return value ?? string.Empty;
    }

}
