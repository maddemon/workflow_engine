using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 工作流工具节点，作为 Agent 的工具调用子工作流。
/// 支持从数据库引用已有工作流或内嵌 JSON 定义。
/// </summary>
[NodeMeta(TypeName = "workflowTool", DisplayName = "Workflow Tool", Category = NodeCategory.AI, Icon = "layers", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
[Port(FlowConstants.PortNames.Tools, "Tool Output", PortDirection.Output, PortType.AgentTool)]
public sealed class SubWorkflowToolNode : NodeBase
{
    [Inject] public INodeRegistry? Registry { get; private set; }
    [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;
    private const int DefaultMaxNestingDepth = 5;
    private const int MinMaxNestingDepth = 1;
    private const int MaxMaxNestingDepth = 20;

    /// <summary>
    /// 工作流来源。
    /// </summary>
    [Description("Where to get the workflow from.")]
    public WorkflowSource Source { get; set; } = WorkflowSource.Inline;

    /// <summary>
    /// 工作流 ID（Database 模式）。
    /// </summary>
    [Description("Workflow ID to execute (when Source is Database).")]
    [DisplayCondition(nameof(Source), WorkflowSource.Database)]
    public string? WorkflowId { get; set; }

    /// <summary>
    /// 内嵌工作流 JSON（Inline 模式）。
    /// </summary>
    [Description("Inline workflow JSON definition (when Source is Inline).")]
    [Hint(PresentationHint.JsonEditor)]
    [DisplayCondition(nameof(Source), WorkflowSource.Inline)]
    public string WorkflowJson { get; set; } = string.Empty;

    /// <summary>
    /// 工具名称（LLM 调用时显示）。
    /// </summary>
    [Description("Tool name that LLM will use to call this workflow.")]
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// 工具描述（帮助 LLM 理解何时调用）。
    /// </summary>
    [Description("Tool description that helps LLM understand when to use this workflow.")]
    public string ToolDescription { get; set; } = string.Empty;

    /// <summary>
    /// 子工作流超时时间（秒）。
    /// </summary>
    [Description("Sub-workflow execution timeout in seconds. Empty means no timeout.")]
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// 最大嵌套深度，防止无限递归。
    /// </summary>
    [Description("Maximum nesting depth to prevent infinite recursion.")]
    [Range(MinMaxNestingDepth, MaxMaxNestingDepth)]
    public int MaxNestingDepth { get; set; } = DefaultMaxNestingDepth;

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (Ctx.NestingDepth >= MaxNestingDepth)
        {
            throw new NodeExecutionException(
                "MaxNestingDepthExceeded",
                $"SubWorkflow nesting depth {Ctx.NestingDepth} exceeds maximum allowed depth of {MaxNestingDepth}.");
        }

        Workflow? workflow = null;

        if (Source == WorkflowSource.Database)
        {
            if (string.IsNullOrWhiteSpace(WorkflowId))
            {
                throw new NodeExecutionException("MissingWorkflowId", "WorkflowId is required when Source is Database.");
            }

            if (!Guid.TryParse(WorkflowId, out var workflowId))
            {
                throw new NodeExecutionException("InvalidWorkflowId", $"WorkflowId '{WorkflowId}' is not a valid GUID.");
            }

            workflow = Ctx.WorkflowLoader is null ? null : await Ctx.WorkflowLoader.LoadAsync(workflowId, ct).ConfigureAwait(false);
            if (workflow is null)
            {
                throw new NodeExecutionException("WorkflowNotFound", $"Workflow '{WorkflowId}' not found in database.");
            }
        }
        else // Inline
        {
            if (string.IsNullOrWhiteSpace(WorkflowJson))
            {
                throw new NodeExecutionException("MissingWorkflowJson", "WorkflowJson is required when Source is Inline.");
            }

            if (!JsonHelper.TryParse<Workflow>(WorkflowJson, out workflow, out var parseError, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }))
            {
                throw new NodeExecutionException("InvalidWorkflowJson", $"Failed to parse workflow JSON: {parseError}");
            }
        }

        var validationError = WorkflowValidator.EnsureNonEmpty(workflow);
        if (validationError is not null)
        {
            throw new NodeExecutionException("EmptyWorkflow", validationError);
        }

        using var timeoutCts = TimeoutSeconds.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        if (timeoutCts is not null && TimeoutSeconds.HasValue)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds.Value));
        }

        var effectiveToken = timeoutCts?.Token ?? ct;

        try
        {
            var executor = new SubWorkflowExecutor(Registry, Ctx.NestingDepth + 1);
            var inputBatch = input.InputBatch;
            var inputPayload = inputBatch.Items.Count > 0 ? inputBatch.Items[0].Data : null;
            var result = await executor.ExecuteAsync(workflow!, inputPayload, effectiveToken).ConfigureAwait(false);

            if (!result.Success)
            {
                throw new NodeExecutionException(result.Error!.Code, result.Error!.Message);
            }

            return NodeHandlerOutput.Data(result.Output);
        }
        catch (OperationCanceledException) when (timeoutCts is not null)
        {
            throw new NodeExecutionException("SubWorkflowTimeout", "Sub-workflow execution timed out.");
        }
        catch (NodeExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NodeExecutionException("SubWorkflowError", $"Sub-workflow execution failed: {ex.Message}");
        }
    }

}

/// <summary>
/// 工作流来源。
/// </summary>
public enum WorkflowSource
{
    /// <summary>从数据库引用</summary>
    Database,

    /// <summary>内嵌 JSON 定义</summary>
    Inline
}