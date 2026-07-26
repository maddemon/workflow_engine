using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 思考工具节点，作为 Agent 的工具记录推理过程。
/// </summary>
[NodeMeta(TypeName = "thinkTool", DisplayName = "Think Tool", Category = NodeCategory.AI, Icon = "brain", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
[Port(FlowConstants.PortNames.Tools, "Tool Output", PortDirection.Output, PortType.AgentTool)]
public sealed class ThinkToolNode : NodeBase
{
    /// <summary>
    /// 工具描述（帮助 LLM 理解何时使用）。
    /// </summary>
    [Description("Tool description that helps LLM understand when to use this tool for thinking.")]
    public string ToolDescription { get; set; } = "Use this tool to think about something. It will not obtain new information or change the database, but just append the thought to the log. Use it when complex reasoning or some cache memory is needed.";

    /// <summary>
    /// 待记录的思考内容。JS 表达式，支持 <c>$json</c> / <c>$input</c>。
    /// 输入批次读取之后回退到此属性（不再读取被合规规则禁止的已解析参数字典）。
    /// </summary>
    [Description("Thought content to record. JS expression; supports $json/$input. Falls back to the Thought property when no input is provided.")]
    public string Thought { get; set; } = string.Empty;

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            var thought = GetThought(input);
            if (string.IsNullOrWhiteSpace(thought))
            {
                throw new NodeExecutionException("MissingThought", "Thinking content is required.");
            }

            Logger?.LogInformation("[Think] {Thought}", thought);

            var outputBatch = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject
                        {
                            ["thought"] = thought,
                            ["timestamp"] = DateTime.UtcNow.ToString("o")
                        },
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            };

            return NodeHandlerOutput.Data(outputBatch);
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            throw new NodeExecutionException("ThinkError", $"Thinking failed: {ex.Message}");
        }
    }

    private string? GetThought(NodeInput input)
    {
        var batch = input.InputBatch;
        if (batch.Items.Count > 0)
        {
            var data = batch.Items[0].Data;
            if (data is JsonObject obj)
            {
                if (obj.TryGetPropertyValue("thought", out var thoughtVal))
                {
                    return thoughtVal?.ToString();
                }
                if (obj.TryGetPropertyValue("thinking", out var thinkingVal))
                {
                    return thinkingVal?.ToString();
                }
                if (obj.TryGetPropertyValue("content", out var contentVal))
                {
                    return contentVal?.ToString();
                }
                if (obj.TryGetPropertyValue("input", out var inputVal))
                {
                    return inputVal?.ToString();
                }
            }
            else if (data is JsonValue val)
            {
                return val.ToString();
            }
        }

        // 回退到绑定属性（不读取被合规规则禁止的已解析参数字典）。
        if (!string.IsNullOrWhiteSpace(Thought))
        {
            return Thought;
        }

        return null;
    }
}
