using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 去重节点，移除重复的数据项。
/// </summary>
[NodeMeta(TypeName = "deduplicate", DisplayName = "Remove Duplicates", Category = NodeCategory.Data, Icon = "filter-1", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class DeduplicateNode : NodeBase
{
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
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var inputBatch = input.InputBatch;

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

        return Task.FromResult(NodeHandlerOutput.Data(new DataBatch { Items = outputItems }));
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
