using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// AI Agent 节点，通过 LLM 循环调用下游工具节点完成任务。
/// </summary>
public sealed class AgentNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "agent";

    /// <inheritdoc />
    public string DisplayName => "Agent";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "brain";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 最大 LLM 迭代次数。
    /// </summary>
    [Description("Maximum number of LLM iterations before forced termination.")]
    public int MaxIterations { get; set; } = 10;

    /// <summary>
    /// LLM 调用超时时间（秒）。
    /// </summary>
    [Description("LLM call timeout in seconds. Empty means no timeout.")]
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// 系统提示词模板。
    /// </summary>
    [Description("System prompt template for the LLM.")]
    public string PromptTemplate { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = "tools", DisplayName = "Tools", Direction = PortDirection.Output, Type = PortType.AgentTool },
        new PortDefinition { Name = "llmSupply", DisplayName = "LLM Supply", Direction = PortDirection.Input, Type = PortType.LLMSupply }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var llmClient = context.LlmClient;
        if (llmClient is null)
        {
            return context.ErrorResult("MissingLlmClient", "LLM client not available. Connect an LLM supply node.");
        }

        var tools = CollectTools(context);
        var messages = BuildMessages(context);

        var maxIterations = MaxIterations > 0 ? MaxIterations : 10;
        using var timeoutCts = TimeoutSeconds.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;

        if (timeoutCts is not null && TimeoutSeconds.HasValue)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds.Value));
        }

        var effectiveToken = timeoutCts?.Token ?? cancellationToken;
        var lastContent = string.Empty;

        for (var i = 0; i < maxIterations; i++)
        {
            if (effectiveToken.IsCancellationRequested)
            {
                return CreateTimeoutResult("LLM call timed out or was cancelled.", context);
            }

            LlmResponse response;
            try
            {
                response = await llmClient.ChatAsync(messages, tools, effectiveToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts is not null)
            {
                return CreateTimeoutResult("LLM call timed out.", context);
            }
            catch (Exception ex)
            {
                return context.ErrorResult("LlmError", $"LLM call failed: {ex.Message}");
            }

            lastContent = response.Content ?? string.Empty;

            if (!response.HasToolCalls)
            {
                return CreateSuccessResult(lastContent, context);
            }

            messages.Add(new LlmMessage
            {
                Role = "assistant",
                Content = response.Content,
                ToolCalls = response.ToolCalls
            });

            foreach (var toolCall in response.ToolCalls!)
            {
                var toolResult = await ExecuteToolAsync(toolCall, tools, context, effectiveToken).ConfigureAwait(false);
                messages.Add(new LlmMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = toolResult
                });
            }
        }

        return CreateTimeoutResult($"Maximum iterations ({maxIterations}) reached.", context);
    }

    /// <summary>
    /// 扫描 Agent 工具端口连接的下游 tool 节点，生成工具定义列表。
    /// </summary>
    internal IReadOnlyList<ToolDefinition> CollectTools(NodeExecutionContext context)
    {
        var workflow = context.Workflow;
        var currentNodeId = context.Node.Id;

        var toolConnections = workflow.Connections
            .Where(c => c.SourceNodeId == currentNodeId && c.SourcePortName == "tools")
            .ToList();

        if (toolConnections.Count == 0)
        {
            return [];
        }

        var tools = new List<ToolDefinition>();
        foreach (var connection in toolConnections)
        {
            var toolNode = workflow.Nodes.FirstOrDefault(n => n.Id == connection.TargetNodeId);
            if (toolNode is null)
            {
                continue;
            }

            INodeType? nodeType = null;
            if (context.NodeRegistry?.TryGet(toolNode.TypeName, out var resolvedType) == true)
            {
                nodeType = resolvedType;
            }

            if (nodeType is null)
            {
                continue;
            }

            NodeTypeDescriptor? descriptor = null;
            try
            {
                descriptor = context.NodeRegistry?.GetDescriptor(toolNode.TypeName);
            }
            catch (InvalidOperationException)
            {
                // Descriptor not found, skip
            }

            var parametersSchema = BuildParametersSchema(descriptor?.Parameters);

            tools.Add(new ToolDefinition
            {
                Name = toolNode.Name,
                Description = nodeType.DisplayName,
                TargetNodeDefinitionId = toolNode.Id,
                ParametersSchema = parametersSchema
            });
        }

        return tools;
    }

    private static JsonObject? BuildParametersSchema(IReadOnlyList<ParameterDefinition>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }

        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var param in parameters)
        {
            var prop = new JsonObject
            {
                ["type"] = MapParameterType(param.Type),
                ["description"] = param.Description ?? param.DisplayName
            };

            if (param.Options.Count > 0)
            {
                var enumArray = new JsonArray();
                foreach (var option in param.Options)
                {
                    enumArray.Add(JsonValue.Create(option.Value.ToString()));
                }
                prop["enum"] = enumArray;
            }

            properties[param.Name] = prop;

            if (param.Required)
            {
                required.Add(param.Name);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    private static string MapParameterType(ParameterType type) => type switch
    {
        ParameterType.String => "string",
        ParameterType.Number => "number",
        ParameterType.Boolean => "boolean",
        ParameterType.Json => "object",
        ParameterType.Array => "array",
        _ => "string"
    };

    private List<LlmMessage> BuildMessages(NodeExecutionContext context)
    {
        var messages = new List<LlmMessage>();

        if (!string.IsNullOrWhiteSpace(PromptTemplate))
        {
            messages.Add(new LlmMessage { Role = "system", Content = PromptTemplate });
        }

        var inputJson = SerializeInput(context);
        if (inputJson is not null)
        {
            messages.Add(new LlmMessage { Role = "user", Content = inputJson });
        }

        return messages;
    }

    private static string? SerializeInput(NodeExecutionContext context)
    {
        if (!context.Inputs.TryGetValue("input", out var batch) || batch.Items.Count == 0)
        {
            return null;
        }

        var firstItem = batch.Items[0];
        if (firstItem.Data is null)
        {
            return null;
        }

        return firstItem.Data.ToJsonString();
    }

    private async Task<string> ExecuteToolAsync(
        LlmToolCall toolCall,
        IReadOnlyList<ToolDefinition> tools,
        NodeExecutionContext parentContext,
        CancellationToken cancellationToken)
    {
        var tool = tools.FirstOrDefault(t => t.Name == toolCall.Name);
        if (tool is null)
        {
            return $"Tool '{toolCall.Name}' not found.";
        }

        var toolNode = parentContext.Workflow.Nodes.FirstOrDefault(n => n.Id == tool.TargetNodeDefinitionId);
        if (toolNode is null)
        {
            return $"Tool node '{tool.TargetNodeDefinitionId}' not found.";
        }

        if (parentContext.NodeRegistry?.TryGet(toolNode.TypeName, out var nodeType) != true || nodeType is null)
        {
            return $"Node type '{toolNode.TypeName}' not found.";
        }

        JsonNode? args;
        try
        {
            args = JsonNode.Parse(toolCall.Arguments);
        }
        catch
        {
            args = null;
        }

        var inputBatch = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = args,
                    Success = true,
                    SourceIndex = 0
                }
            ]
        };

        var toolContext = new NodeExecutionContext
        {
            Workflow = parentContext.Workflow,
            ExecutionId = parentContext.ExecutionId,
            Node = new NodeDefinition
            {
                Id = toolNode.Id,
                TypeName = toolNode.TypeName,
                Name = toolNode.Name,
                Parameters = toolNode.Parameters,
                Ports = toolNode.Ports
            },
            Inputs = new Dictionary<string, DataBatch> { ["input"] = inputBatch },
            RawParameters = toolNode.Parameters,
            ResolvedParameters = toolNode.Parameters,
            Credentials = parentContext.Credentials,
            Logger = parentContext.Logger,
            CancellationToken = cancellationToken
        };

        try
        {
            var result = await nodeType.ExecuteAsync(toolContext, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return $"Tool execution failed: {result.Error?.Message ?? "Unknown error"}";
            }

            if (result.Output.Items.Count > 0)
            {
                var data = result.Output.Items[0].Data;
                if (data is not null)
                {
                    return data.ToJsonString();
                }
            }

            return "Tool executed successfully.";
        }
        catch (Exception ex)
        {
            return $"Tool execution error: {ex.Message}";
        }
    }

    private static NodeExecutionResult CreateSuccessResult(string content, NodeExecutionContext context)
    {
        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = content,
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            }
        };
    }

    private static NodeExecutionResult CreateTimeoutResult(string message, NodeExecutionContext context)
    {
        return new NodeExecutionResult
        {
            Success = false,
            Error = new NodeError
            {
                Code = "AgentTimeout",
                Message = message,
                NodeDefinitionId = context.Node.Id
            }
        };
    }
}
