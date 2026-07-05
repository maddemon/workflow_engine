using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 编辑字段节点，用于添加、修改或删除数据字段。
/// </summary>
public sealed class SetNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "set";

    /// <inheritdoc />
    public string DisplayName => "Edit Fields (Set)";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "edit";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 要设置的字段列表。
    /// </summary>
    [Description("Fields to set. Use dot notation for nested fields (e.g. 'address.city').")]
    public List<SetField> Fields { get; set; } = [];

    /// <summary>
    /// 包含模式：all 保留所有字段，selected 只保留指定字段，exclude 移除指定字段。
    /// </summary>
    [Description("Which fields to include in the output.")]
    public SetIncludeMode Include { get; set; } = SetIncludeMode.All;

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

        var outputItems = new List<DataItem>();

        foreach (var inputItem in inputBatch.Items)
        {
            JsonObject outputObj;

            if (Include == SetIncludeMode.All)
            {
                outputObj = inputItem.Data is JsonObject existing
                    ? existing.DeepClone().AsObject()
                    : new JsonObject();
            }
            else if (Include == SetIncludeMode.Selected)
            {
                outputObj = new JsonObject();
            }
            else // Exclude
            {
                outputObj = inputItem.Data is JsonObject existing
                    ? existing.DeepClone().AsObject()
                    : new JsonObject();
            }

            foreach (var field in Fields)
            {
                var value = ParseValue(field.Value);

                if (Include == SetIncludeMode.Exclude)
                {
                    RemoveNestedField(outputObj, field.Name);
                }
                else
                {
                    SetNestedField(outputObj, field.Name, value);
                }
            }

            outputItems.Add(new DataItem
            {
                Data = outputObj,
                Success = true,
                SourceIndex = inputItem.SourceIndex
            });
        }

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = outputItems }
        });
    }

    private static JsonNode? ParseValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return JsonValue.Create(string.Empty);
        }

        // Try to parse as JSON
        try
        {
            return JsonNode.Parse(value);
        }
        catch
        {
            // Not valid JSON, treat as string
        }

        // Try to parse as number
        if (double.TryParse(value, out var number))
        {
            return JsonValue.Create(number);
        }

        // Try to parse as boolean
        if (bool.TryParse(value, out var boolean))
        {
            return JsonValue.Create(boolean);
        }

        return JsonValue.Create(value);
    }

    private static void SetNestedField(JsonObject obj, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        var current = obj;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetPropertyValue(parts[i], out var next) || next is not JsonObject nextObj)
            {
                nextObj = new JsonObject();
                current[parts[i]] = nextObj;
            }
            current = nextObj;
        }

        current[parts[^1]] = value;
    }

    private static void RemoveNestedField(JsonObject obj, string path)
    {
        var parts = path.Split('.');
        JsonObject? current = obj;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current.TryGetPropertyValue(parts[i], out var next) && next is JsonObject nextObj)
            {
                current = nextObj;
            }
            else
            {
                return; // Path doesn't exist
            }
        }

        current.Remove(parts[^1]);
    }
}

/// <summary>
/// Set 节点的字段定义。
/// </summary>
public sealed class SetField
{
    /// <summary>
    /// 字段名称（支持点号分隔的嵌套路径）。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 字段值（字符串形式，会自动转换类型）。
    /// </summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Set 节点的包含模式。
/// </summary>
public enum SetIncludeMode
{
    /// <summary>保留所有字段</summary>
    All,

    /// <summary>只保留指定字段</summary>
    Selected,

    /// <summary>移除指定字段</summary>
    Exclude
}
