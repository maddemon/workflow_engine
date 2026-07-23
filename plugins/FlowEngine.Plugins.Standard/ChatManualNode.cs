using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 聊天手动输入节点。作为工作流入口（Trigger），接收编辑器在聊天窗口手动输入的聊天消息，
/// 经现有 execute 端点投递为触发负载，将其首个输入项聚合为输出 payload，并补充触发时间。
/// </summary>
/// <remarks>
/// 与计划原本"Output only"的设想不同，本节点必须声明 <see cref="FlowConstants.PortNames.Input"/> 端口：
/// 调度器（WorkflowSchedulerKernel.EnqueueEntryNodesAsync）仅当节点存在输入端口时，才会把触发负载
/// 写入 inputs[inputPorts[0]]。手动输入的聊天消息须经 inputs["Input"] 进入节点，故保留 Input 端口。
/// 本节点是 <c>ChatInputNode</c> 的手动变体：不暴露外部端点，不引入 ResponseMode/WelcomeMessage 参数。
/// </remarks>
public sealed class ChatManualNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "chatManual";

    /// <inheritdoc />
    AiNodeDefinition? INodeType.GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Chat Manual", "Trigger", true,
            "聊天手动输入触发器：接收编辑器在聊天窗口手动输入的聊天消息，经 execute 端点投递为触发负载，聚合首个输入项为输出并附加触发时间，是聊天型工作流的入口节点。",
            ["chat", "manual", "trigger"],
            null,
            AiDefinitionHelpers.Example("聊天消息手动触发",
                JsonNode.Parse("""{"chat":{"message":"帮我查下天气"}}"""),
                JsonNode.Parse("""{"message":"帮我查下天气","triggeredAt":"2026-07-12T09:00:00Z"}""")));

    /// <inheritdoc />
    public string DisplayName => "Chat Manual";

    /// <inheritdoc />
    public string Category => "Trigger";

    /// <inheritdoc />
    public string Icon => "chat";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

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

            // 仅记录触发事件，不记录消息原文（敏感信息避免落日志）。
            context.Logger?.LogInformation("Chat manual triggered.");

            return Task.FromResult(context.Ok(payload));
        }
        catch (Exception ex)
        {
            return Task.FromResult(context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, ex.Message));
        }
    }
}
