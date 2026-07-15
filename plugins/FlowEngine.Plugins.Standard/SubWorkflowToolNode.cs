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
/// 工作流工具节点，作为 Agent 的工具调用子工作流。
/// 支持从数据库引用已有工作流或内嵌 JSON 定义。
/// </summary>
public sealed class SubWorkflowToolNode : INodeType
{
    private const int DefaultMaxNestingDepth = 5;
    private const int MinMaxNestingDepth = 1;
    private const int MaxMaxNestingDepth = 20;

    /// <inheritdoc />
    public string TypeName => "workflowTool";

    /// <inheritdoc />
    public string DisplayName => "Workflow Tool";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "layers";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

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
    public int MaxNestingDepth { get; set; } = DefaultMaxNestingDepth;

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
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var effectiveMaxNestingDepth = ResolveMaxNestingDepth(context);

        if (context.NestingDepth >= effectiveMaxNestingDepth)
        {
            return context.ErrorResult(
                "MaxNestingDepthExceeded",
                $"SubWorkflow nesting depth {context.NestingDepth} exceeds maximum allowed depth of {effectiveMaxNestingDepth}.");
        }

        Workflow? workflow = null;

        if (Source == WorkflowSource.Database)
        {
            if (string.IsNullOrWhiteSpace(WorkflowId))
            {
                return context.ErrorResult("MissingWorkflowId", "WorkflowId is required when Source is Database.");
            }

            if (!Guid.TryParse(WorkflowId, out var workflowId))
            {
                return context.ErrorResult("InvalidWorkflowId", $"WorkflowId '{WorkflowId}' is not a valid GUID.");
            }

            if (context.WorkflowLoader is null)
            {
                return context.ErrorResult("WorkflowLoaderNotAvailable", "Workflow loader is not available in the current execution context.");
            }

            workflow = await context.WorkflowLoader.LoadAsync(workflowId, cancellationToken).ConfigureAwait(false);
            if (workflow is null)
            {
                return context.ErrorResult("WorkflowNotFound", $"Workflow '{WorkflowId}' not found in database.");
            }
        }
        else // Inline
        {
            if (string.IsNullOrWhiteSpace(WorkflowJson))
            {
                return context.ErrorResult("MissingWorkflowJson", "WorkflowJson is required when Source is Inline.");
            }

            if (!context.TryParseJson<Workflow>(WorkflowJson, out workflow, out var parseError, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }))
            {
                return context.ErrorResult("InvalidWorkflowJson", $"Failed to parse workflow JSON: {parseError}");
            }
        }

        var validationError = WorkflowValidator.EnsureNonEmpty(workflow);
        if (validationError is not null)
        {
            return context.ErrorResult("EmptyWorkflow", validationError);
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
            var executor = new SubWorkflowExecutor(context.NodeRegistry, context.NestingDepth + 1);
            var inputBatch = context.GetInputBatch();
            var inputPayload = inputBatch.Items.Count > 0 ? inputBatch.Items[0].Data : null;
            var result = await executor.ExecuteAsync(workflow!, inputPayload, effectiveToken).ConfigureAwait(false);
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

    /// <summary>
    /// 从节点参数解析最大嵌套深度，未配置时使用属性默认值，并校验范围 [1, 20]。
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
