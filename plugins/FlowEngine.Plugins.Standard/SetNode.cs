using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.Exceptions;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 编辑字段节点，用于添加、修改或删除数据字段。
/// </summary>
[NodeMeta(TypeName = "set", DisplayName = "Edit Fields (Set)", Category = NodeCategory.Data, Icon = "edit", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class SetNode : NodeBase
{
    [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;
    [Inject] public IExecutionLogger? Logger { get; private set; }
    /// <inheritdoc />
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Edit Fields (Set)", "Core", false,
            "编辑数据字段：新增、修改或删除字段，支持点号表示嵌套字段（如 address.city）。常用于为下游节点准备/重命名数据。默认保留全部字段。",
            ["core", "transform", "set"],
            JsonNode.Parse("""{"type":"object","description":"与输入结构一致，按 Fields 规则修改后的数据"}"""),
            AiDefinitionHelpers.Example("写入 greeting 与 count 字段",
                JsonNode.Parse("""{"fields":[{"name":"greeting","value":"hello"},{"name":"count","value":3}]}"""),
                JsonNode.Parse("""{"greeting":"hello","count":3}""")));

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
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var inputBatch = input.InputBatch;

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

            foreach (var field in Fields ?? [])
            {
                var value = await EvaluateFieldValueAsync(field.Value, inputItem.Data, inputItem.SourceIndex, ct)
                    .ConfigureAwait(false);

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

        return NodeHandlerOutput.Data(new DataBatch { Items = outputItems });
    }

    /// <summary>
    /// 求值 SetField 的值：空源码返回空字符串；合法 JSON 字面量（字符串/数字/布尔）按字面量处理，
    /// 保持旧版纯字符串值与脚本简写语义；否则作为 JS 表达式按当前 item 逐项求值（如 <c>$json.userid</c>）。
    /// 表达式求值失败时退化为字面量字符串（容错）。
    /// </summary>
    private async Task<JsonNode?> EvaluateFieldValueAsync(
        Script? script, JsonNode? item, int index, CancellationToken cancellationToken)
    {
        var source = script?.Source;
        if (string.IsNullOrEmpty(source))
        {
            return JsonValue.Create(string.Empty);
        }

        if (TryParseJsonLiteral(source!, out var literal) && literal is not null)
        {
            return literal.DeepClone();
        }

        try
        {
            return await script!.EvaluateAsync<JsonNode>(Ctx, item: item, itemIndex: index, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ScriptErrorException ex)
        {
            Logger?.LogWarning(
                "SetNode 字段值表达式求值失败，已回退为字面量字符串。Source: {Source}, Error: {Error}",
                source,
                ex.Message);
            return JsonValue.Create(source);
        }
    }

    private static bool TryParseJsonLiteral(string source, out JsonNode? literal)
    {
        literal = null;
        try
        {
            var node = JsonNode.Parse(source);
            if (node is JsonValue)
            {
                literal = node;
                return true;
            }
        }
        catch
        {
            // 非合法 JSON 字面量，按表达式处理
        }

        return false;
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
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 字段值。支持两种写法：
    /// <list type="bullet">
    ///   <item>纯字面量（字符串/数字/布尔）或纯字符串简写：直接作为字面量值，保持向后兼容。</item>
    ///   <item>JS 表达式（如 <c>$json.userid</c>、<c>$json.name + ' (' + $json.dept + ')'</c>）：按当前 item 逐项求值。</item>
    /// </list>
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("value")]
    public Script Value { get; set; } = Script.Empty;
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