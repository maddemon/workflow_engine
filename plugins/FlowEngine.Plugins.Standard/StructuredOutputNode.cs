using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 结构化输出节点。将上游（通常为 LLM 节点）产出的文本 JSON 解析为结构化 <see cref="DataItem"/>，
/// 并可按 JSON Schema 的 <c>required</c> 键做轻量校验。本节点不调用 LLM，仅做解析/校验（符合 §3.3.3：不调用 LLM）。
/// </summary>
[NodeMeta(TypeName = "structuredOutput", DisplayName = "Structured Output", Category = NodeCategory.AI, Icon = "structured", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class StructuredOutputNode : NodeBase
{
    [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;
    [Inject] public IExecutionLogger? Logger { get; private set; }
    /// <summary>
    /// 待解析的原始文本（通常为 LLM 输出）。JS 表达式，支持 <c>$json</c> / <c>$input</c>。示例：<c>$json.text</c>。必填。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("Raw text to parse (typically LLM output). JS expression; supports $json/$input. Example: $json.text")]
    public Script? Input { get; set; }

    /// <summary>
    /// 可选 JSON Schema（字符串）。提供时校验 <c>required</c> 键是否存在；可选按 <c>properties[type]</c> 做基本类型核对。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("Optional JSON Schema (string). When provided, required keys are validated.")]
    public Script? Schema { get; set; }

    /// <summary>
    /// 为 true 时，缺失必填键（按 Schema）或类型不符将导致节点失败；为 false 时跳过校验（仅解析）。默认 true。
    /// </summary>
    [Description("When true, missing required keys (per Schema) fail the node. Default true.")]
    public bool Strict { get; set; } = true;

    /// <inheritdoc />
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Structured Output", "AI", false,
            "将上游（通常为 LLM 节点）的文本 JSON 输出解析为结构化数据，并可按 JSON Schema 校验必填字段。本节点不调用 LLM，仅做解析与轻量校验。",
            ["ai", "transform", "json", "structured"],
            JsonNode.Parse("{\"type\":\"object\",\"description\":\"与输入文本解析后的 JSON 对象结构一致\"}"),
            AiDefinitionHelpers.Example("解析 LLM 输出为对象",
                JsonNode.Parse("{\"input\":\"$json.text\",\"schema\":\"{\\\"required\\\":[\\\"name\\\"]}\"}"),
                JsonNode.Parse("{\"name\":\"Alice\"}")));

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            // OnceForAll：取首个输入项作为 $json 上下文（无输入则为 null，交由表达式求值处理）。
            var inputBatch = input.InputBatch;
            var evalItem = inputBatch.Items.Count > 0 ? inputBatch.Items[0].Data : null;

            // 1) 解析 Input 文本
            var text = Input is not null
                ? await Input.EvaluateAsync<string>(Ctx, item: evalItem, itemIndex: 0, cancellationToken: ct).ConfigureAwait(false)
                : null;

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new NodeExecutionException("MissingInput", "Input 表达式求值结果为空，无法解析结构化输出。");
            }

            // 2) 解析为 JSON 对象（仅对象合法；数组/数值/字符串/布尔/null 视为 InvalidJson）
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new NodeExecutionException("InvalidJson", $"输入不是合法 JSON：{ex.Message}");
            }

            if (parsed is not JsonObject jsonObject)
            {
                throw new NodeExecutionException("InvalidJson", "输入 JSON 不是对象（应为键值对对象）。");
            }

            // 3) 可选 Schema 校验（仅 required 必填键 + 可选 properties 基本类型核对）
            if (Schema is not null)
            {
                var schemaText = await Schema.EvaluateAsync<string>(Ctx, item: evalItem, itemIndex: 0, cancellationToken: ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(schemaText))
                {
                    JsonNode? schemaParsed;
                    try
                    {
                        schemaParsed = JsonNode.Parse(schemaText);
                    }
                    catch (JsonException ex)
                    {
                        throw new NodeExecutionException("InvalidJson", $"Schema 不是合法 JSON：{ex.Message}");
                    }

                    if (schemaParsed is JsonObject schemaObj)
                    {
                        var errors = ValidateAgainstSchema(jsonObject, schemaObj);
                        if (errors.Count > 0 && Strict)
                        {
                            throw new NodeExecutionException("SchemaValidationFailed", string.Join("; ", errors));
                        }
                    }
                }
            }

            Logger?.LogInformation("structuredOutput 解析成功，字段数 {Count}。", jsonObject.Count);
            return NodeHandlerOutput.Data(new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = jsonObject,
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            });
        }
        catch (OperationCanceledException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "操作已取消。");
        }
        catch (ScriptErrorException ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.ScriptError, $"表达式求值失败：{ex.Message}");
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            // 仅记录非敏感信息，绝不输出原始文本（可能含敏感内容）。
            Logger?.LogError(ex, "structuredOutput 发生意外错误。");
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"意外错误：{ex.Message}");
        }
    }

    /// <summary>
    /// 按 JSON Schema 轻量校验：校验 <c>required</c> 键是否存在；若字段存在且 <c>properties[type]</c> 指定类型，则做基本类型核对。
    /// 不做完整 draft 校验。返回错误信息列表（为空表示通过）。
    /// </summary>
    /// <param name="data">已解析的数据对象。</param>
    /// <param name="schema">解析后的 JSON Schema 对象。</param>
    private static List<string> ValidateAgainstSchema(JsonObject data, JsonObject schema)
    {
        var errors = new List<string>();

        if (schema.TryGetPropertyValue("required", out var requiredNode) && requiredNode is JsonArray required)
        {
            foreach (var item in required)
            {
                if (item is JsonValue value && value.TryGetValue<string>(out var key) && !data.ContainsKey(key))
                {
                    errors.Add($"缺少必填字段 '{key}'");
                }
            }
        }

        if (schema.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject props)
        {
            foreach (var prop in props)
            {
                if (prop.Value is not JsonObject propDef) continue;
                if (!propDef.TryGetPropertyValue("type", out var typeNode) || typeNode is not JsonValue typeVal) continue;
                if (!data.ContainsKey(prop.Key)) continue; // 缺失由 required 负责
                var expected = typeVal.GetValue<string>();
                if (!TypeMatches(data[prop.Key]!, expected))
                {
                    errors.Add($"字段 '{prop.Key}' 类型不符，期望 {expected}");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// 基本 JSON 类型核对，用于 Schema <c>properties[type]</c> 校验。未知类型视为通过。
    /// </summary>
    private static bool TypeMatches(JsonNode value, string expected) => expected switch
    {
        "string" => value is JsonValue v && v.TryGetValue<string>(out _),
        "number" => value is JsonValue v && (v.TryGetValue<double>(out _) || v.TryGetValue<long>(out _)),
        "integer" => value is JsonValue v && (v.TryGetValue<long>(out _) || v.TryGetValue<int>(out _)),
        "boolean" => value is JsonValue v && v.TryGetValue<bool>(out _),
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "null" => value is null,
        _ => true
    };
}