using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// Webhook 节点，接收外部 HTTP 请求并触发工作流。
/// </summary>
public sealed class WebhookNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "webhook";

    /// <inheritdoc />
    public string DisplayName => "Webhook";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "webhook";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// HTTP 方法。
    /// </summary>
    [Description("HTTP method to listen for.")]
    public WebhookMethod Method { get; set; } = WebhookMethod.Post;

    /// <summary>
    /// Webhook 路径。
    /// </summary>
    [Description("Path for the webhook URL (e.g. 'my-webhook').")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 响应模式。
    /// </summary>
    [Description("When to respond to the webhook.")]
    public WebhookResponseMode ResponseMode { get; set; } = WebhookResponseMode.Immediately;

    /// <summary>
    /// 响应数据（LastNode 模式下使用）。
    /// </summary>
    [Description("Data to respond with (for LastNode mode). Use expressions to reference previous nodes.")]
    [Hint(PresentationHint.TextArea)]
    public string ResponseData { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => true;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        // In a real implementation, this node would be triggered by an HTTP request
        // For now, we'll simulate receiving data from the trigger payload

        var triggerPayload = GetTriggerPayload(context);

        var outputBatch = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = triggerPayload ?? new JsonObject
                    {
                        ["method"] = Method.ToString().ToUpperInvariant(),
                        ["path"] = Path,
                        ["headers"] = new JsonObject(),
                        ["query"] = new JsonObject(),
                        ["body"] = new JsonObject()
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

    private JsonNode? GetTriggerPayload(NodeExecutionContext context)
    {
        // Check if there's trigger data in the context
        if (context.Inputs.TryGetValue("trigger", out var triggerBatch) && triggerBatch.Items.Count > 0)
        {
            return triggerBatch.Items[0].Data;
        }

        return null;
    }
}

/// <summary>
/// Webhook HTTP 方法。
/// </summary>
public enum WebhookMethod
{
    Get,
    Post,
    Put,
    Delete,
    Patch
}

/// <summary>
/// Webhook 响应模式。
/// </summary>
public enum WebhookResponseMode
{
    /// <summary>立即响应</summary>
    Immediately,

    /// <summary>等待最后一个节点完成</summary>
    LastNode,

    /// <summary>等待工作流完成</summary>
    WhenLastNodeFinishes
}
