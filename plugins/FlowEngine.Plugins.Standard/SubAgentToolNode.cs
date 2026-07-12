using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 子 Agent 工具节点，作为 Agent 的工具调用嵌套执行另一个 Agent。
/// </summary>
public sealed class SubAgentToolNode : INodeType
{
    private const int DefaultMaxNestingDepth = 3;
    private const int DefaultMaxIterations = 10;
    private const int MinMaxNestingDepth = 1;
    private const int MaxMaxNestingDepth = 10;
    private const int MinMaxIterations = 1;
    private const int MaxMaxIterations = 100;

    /// <inheritdoc />
    public string TypeName => "subAgentTool";

    /// <inheritdoc />
    public string DisplayName => "Sub-Agent Tool";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "robot";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 嵌套 Agent 的系统提示词。
    /// </summary>
    [Description("System prompt for the nested agent.")]
    public string PromptTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 最大嵌套深度。
    /// </summary>
    [Description("Maximum nesting depth to prevent infinite recursion.")]
    public int MaxNestingDepth { get; set; } = DefaultMaxNestingDepth;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool", Direction = PortDirection.Input, Type = PortType.AgentTool },
        new PortDefinition { Name = FlowConstants.PortNames.Llm, DisplayName = "LLM", Direction = PortDirection.Input, Type = PortType.LLM }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var effectiveMaxNestingDepth = ResolveMaxNestingDepth(context);

        if (context.NestingDepth >= effectiveMaxNestingDepth)
        {
            return context.ErrorResult(
                "MaxNestingDepthExceeded",
                $"Agent nesting depth {context.NestingDepth} exceeds maximum allowed depth of {effectiveMaxNestingDepth}.");
        }

        var llmClient = context.LlmClient;
        if (llmClient is null)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.MissingLlmClient, "LLM client not available for nested agent.");
        }

        var tools = CollectTools(context);
        var messages = BuildMessages(context);
        var maxIterations = ResolveMaxIterations(context);

        var parentRecordId = context.NodeExecutionRecordId != Guid.Empty
            ? context.NodeExecutionRecordId
            : context.ExecutionId;

        var resolver = new Core.Agent.InlineResolver(
            llmClient,
            tools,
            context,
            maxIterations,
            parentRecordId: parentRecordId);

        var result = await resolver.RunAsync(messages, cancellationToken).ConfigureAwait(false);

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = result.Content,
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            }
        };
    }

    /// <summary>
    /// 从节点参数解析最大嵌套深度，未配置时使用属性默认值，并校验范围 [1, 10]。
    /// </summary>
    private int ResolveMaxNestingDepth(NodeExecutionContext context)
    {
        if (context.ResolvedParameters.TryGetValue("maxNestingDepth", out var val) &&
            val is JsonValue jsonVal &&
            jsonVal.TryGetValue<int>(out var depth))
        {
            if (depth < MinMaxNestingDepth)
            {
                return MinMaxNestingDepth;
            }

            if (depth > MaxMaxNestingDepth)
            {
                return MaxMaxNestingDepth;
            }

            return depth;
        }

        return MaxNestingDepth;
    }

    /// <summary>
    /// 从节点参数解析最大 LLM 迭代次数，未配置时使用默认值，并校验范围 [1, 100]。
    /// </summary>
    private int ResolveMaxIterations(NodeExecutionContext context)
    {
        if (context.ResolvedParameters.TryGetValue("maxIterations", out var val) &&
            val is JsonValue jsonVal &&
            jsonVal.TryGetValue<int>(out var iterations))
        {
            if (iterations < MinMaxIterations)
            {
                return MinMaxIterations;
            }

            if (iterations > MaxMaxIterations)
            {
                return MaxMaxIterations;
            }

            return iterations;
        }

        return DefaultMaxIterations;
    }

    private IReadOnlyList<ToolDefinition> CollectTools(NodeExecutionContext context)
    {
        var workflow = context.Workflow;
        var currentNodeId = context.Node.Id;

        var toolConnections = workflow.Connections
            .Where(c => c.TargetNodeId == currentNodeId && c.TargetPortName == FlowConstants.PortNames.Tools)
            .ToList();

        if (toolConnections.Count == 0)
        {
            return [];
        }

        var tools = new List<ToolDefinition>();
        foreach (var connection in toolConnections)
        {
            var toolNode = workflow.Nodes.FirstOrDefault(n => n.Id == connection.SourceNodeId);
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

            var parametersSchema = Core.Tools.SchemaDerivation.DeriveSchema(descriptor?.Parameters);

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

    private List<LlmMessage> BuildMessages(NodeExecutionContext context)
    {
        var messages = new List<LlmMessage>();

        if (!string.IsNullOrWhiteSpace(PromptTemplate))
        {
            messages.Add(new LlmMessage { Role = "system", Content = PromptTemplate });
        }

        var batch = context.GetInputBatch();
        if (batch.Items.Count > 0)
        {
            var firstItem = batch.Items[0];
            if (firstItem.Data is not null)
            {
                messages.Add(new LlmMessage { Role = "user", Content = firstItem.Data.ToJsonString() });
            }
        }

        return messages;
    }
}
