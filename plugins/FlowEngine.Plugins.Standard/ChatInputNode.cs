using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 聊天输入节点。作为工作流入口（Trigger），接收聊天窗口经现有 execute 端点投递的触发负载，
/// 将其首个输入项聚合为输出 payload，并补充触发时间、可选欢迎语与响应模式。
/// </summary>
/// <remarks>
/// 与计划原本"Output only"的设想不同，本节点必须声明 <see cref="FlowConstants.PortNames.Input"/> 端口：
/// 调度器（WorkflowSchedulerKernel.EnqueueEntryNodesAsync）仅当节点存在输入端口时，才会把触发负载
/// 写入 inputs[inputPorts[0]]。聊天消息须经 inputs["Input"] 进入节点，故保留 Input 端口。
/// </remarks>
public sealed class ChatInputNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "chatInput";

    /// <inheritdoc />
    AiNodeDefinition? INodeType.GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Chat Input", "Trigger", true,
            "聊天入口触发器：接收聊天窗口经 execute 端点投递的消息负载，聚合首个输入项为输出并附加触发时间、欢迎语与响应模式，是聊天型工作流的入口节点。",
            ["chat", "trigger", "input"],
            null,
            AiDefinitionHelpers.Example("聊天消息触发",
                JsonNode.Parse("""{"chat":{"message":"你好","sessionId":"s1"}}"""),
                JsonNode.Parse("""{"message":"你好","sessionId":"s1","triggeredAt":"2026-07-12T09:00:00Z"}""")));

    /// <inheritdoc />
    public string DisplayName => "Chat Input";

    /// <inheritdoc />
    public string Category => "Trigger";

    /// <inheritdoc />
    public string Icon => "chat";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 响应模式：Full = 完整返回；Streaming = 通过 WebSocket/SSE 增量推送（下游/流式层读取）。
    /// </summary>
    [Description("响应模式：Full=完整返回；Streaming=通过 WebSocket/SSE 增量推送（复用既有流式能力）。")]
    public ChatResponseMode ResponseMode { get; set; } = ChatResponseMode.Full;

    /// <summary>
    /// 可选欢迎语，输出到下游供 LLM 使用。
    /// </summary>
    [Description("可选欢迎语，输出到下游供 LLM 使用。")]
    [Hint(PresentationHint.TextArea)]
    public string? WelcomeMessage { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => true;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var batch = context.GetInputBatch();
            var payload = new JsonObject();

            if (batch.Items.Count > 0)
            {
                var firstData = batch.Items[0].Data;
                if (firstData is JsonObject obj)
                {
                    // 克隆首个输入项的 JSON 对象属性，避免与上游数据产生引用共享。
                    foreach (var kv in obj)
                    {
                        payload[kv.Key] = kv.Value?.DeepClone();
                    }
                }
                else if (firstData is JsonValue)
                {
                    // 标量（字符串/数字）映射到 message 字段。
                    payload["message"] = firstData.DeepClone();
                }
                // 数组/null 等其他类型：payload 保持为空。
            }

            // 仅在输入未自带触发时间时补充（避免覆盖调用方显式提供的值）。
            if (!payload.ContainsKey("triggeredAt"))
            {
                payload["triggeredAt"] = DateTime.UtcNow.ToString("o");
            }

            if (!string.IsNullOrEmpty(WelcomeMessage))
            {
                payload["welcomeMessage"] = WelcomeMessage;
            }

            // Full 为隐式默认模式（与 AI 示例默认输出一致），仅当显式选择 Streaming 时透出，供下游/流式层读取。
            if (ResponseMode != ChatResponseMode.Full)
            {
                payload["responseMode"] = ResponseMode.ToString();
            }

            context.Logger?.LogInformation("Chat input triggered at {TriggeredAt}.", payload["triggeredAt"]!.GetValue<string>());

            return Task.FromResult(context.Ok(payload));
        }
        catch (Exception ex)
        {
            return Task.FromResult(context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, ex.Message));
        }
    }
}

/// <summary>
/// 聊天输入节点的响应模式。
/// </summary>
public enum ChatResponseMode
{
    /// <summary>完整返回：工作流执行完毕后一次性返回最终结果。</summary>
    [Description("完整返回：工作流执行完毕后一次性返回最终结果。")]
    Full,

    /// <summary>流式返回：通过 WebSocket/SSE 增量推送（复用既有流式能力）。</summary>
    [Description("流式返回：通过 WebSocket/SSE 增量推送（复用既有流式能力）。")]
    Streaming
}
