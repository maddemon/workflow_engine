using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 子工作流工具节点，通过嵌入 JSON 定义子工作流，作为 tool 被 Agent 调用。
/// </summary>
public sealed class SubWorkflowToolNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "subWorkflowTool";

    /// <inheritdoc />
    public string DisplayName => "Sub-Workflow Tool";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "layers";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 子工作流 JSON 定义。
    /// </summary>
    [Description("Sub-workflow JSON definition. The workflow will be executed when this tool is called.")]
    [Hint(PresentationHint.JsonEditor)]
    public string WorkflowJson { get; set; } = string.Empty;

    /// <summary>
    /// 子工作流超时时间（秒）。
    /// </summary>
    [Description("Sub-workflow execution timeout in seconds. Empty means no timeout.")]
    public int? TimeoutSeconds { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool Output", Direction = PortDirection.Output, Type = PortType.AgentTool }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkflowJson))
        {
            return context.ErrorResult("MissingWorkflowJson", "WorkflowJson parameter is required.");
        }

        Workflow workflow;
        try
        {
            workflow = JsonSerializer.Deserialize<Workflow>(WorkflowJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Deserialized workflow is null.");
        }
        catch (JsonException ex)
        {
            return context.ErrorResult("InvalidWorkflowJson", $"Failed to parse workflow JSON: {ex.Message}");
        }

        if (workflow.Nodes.Count == 0)
        {
            return context.ErrorResult("EmptyWorkflow", "The sub-workflow contains no nodes.");
        }

        using var timeoutCts = TimeoutSeconds.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;

        if (timeoutCts is not null && TimeoutSeconds.HasValue)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds.Value));
        }

        var effectiveToken = timeoutCts?.Token ?? cancellationToken;

        try
        {
            var executor = new SubWorkflowExecutor(context.NodeRegistry);
            var inputPayload = GetInputPayload(context);
            var result = await executor.ExecuteAsync(workflow, inputPayload, effectiveToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (timeoutCts is not null)
        {
            return context.ErrorResult("SubWorkflowTimeout", "Sub-workflow execution timed out.");
        }
        catch (Exception ex)
        {
            return context.ErrorResult("SubWorkflowError", $"Sub-workflow execution failed: {ex.Message}");
        }
    }

    private static JsonNode? GetInputPayload(NodeExecutionContext context)
    {
        if (!context.Inputs.TryGetValue("input", out var batch) || batch.Items.Count == 0)
        {
            return null;
        }

        return batch.Items[0].Data;
    }
}
