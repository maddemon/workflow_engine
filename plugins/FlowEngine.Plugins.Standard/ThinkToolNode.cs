using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 思考工具节点，作为 Agent 的工具记录推理过程。
/// 参考 n8n 的 ToolThink 设计。
/// </summary>
public sealed class ThinkToolNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "thinkTool";

    /// <inheritdoc />
    public string DisplayName => "Think Tool";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "brain";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 工具描述（帮助 LLM 理解何时使用）。
    /// </summary>
    [Description("Tool description that helps LLM understand when to use this tool for thinking.")]
    public string ToolDescription { get; set; } = "Use this tool to think about something. It will not obtain new information or change the database, but just append the thought to the log. Use it when complex reasoning or some cache memory is needed.";

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool Output", Direction = PortDirection.Output, Type = PortType.AgentTool }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get thinking content from LLM input
            var thought = GetThought(context);
            if (string.IsNullOrWhiteSpace(thought))
            {
                return Task.FromResult(context.ErrorResult("MissingThought", "Thinking content is required."));
            }

            // Log the thought
            context.Logger?.LogInformation("[Think] {Thought}", thought);

            // Return the thought as output
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

            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = outputBatch
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(context.ErrorResult("ThinkError", $"Thinking failed: {ex.Message}"));
        }
    }

    private string? GetThought(NodeExecutionContext context)
    {
        if (context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) && batch.Items.Count > 0)
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

        // Check ResolvedParameters
        if (context.ResolvedParameters.TryGetValue("thought", out var paramThought))
        {
            return paramThought?.ToString();
        }

        return null;
    }
}
