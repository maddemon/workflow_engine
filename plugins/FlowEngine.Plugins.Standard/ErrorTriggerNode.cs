using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 错误触发器节点。作为工作流入口（Trigger），当其它工作流执行失败（由 <c>WorkflowFailedEvent</c> 触发）
/// 时，由 <see cref="FlowEngine.Host.Triggers.ErrorTriggerEventConsumer"/> 启动本工作流，
/// 并把失败工作流 ID 与错误信息作为首条 DataItem 经输入端口传入。
/// 节点将其聚合为输出 payload，并补充触发时间。
/// </summary>
/// <remarks>
/// 必须声明 <see cref="FlowConstants.PortNames.Input"/> 端口：调度器（WorkflowSchedulerKernel）
/// 仅当节点存在输入端口时，才会把触发负载写入 inputs[inputPorts[0]]。
/// </remarks>
[NodeMeta(TypeName = "errorTrigger", DisplayName = "Error Trigger", Category = NodeCategory.Trigger, Icon = "error", DefaultIsEntry = true)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class ErrorTriggerNode : NodeBase
{
    /// <inheritdoc />
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Error Trigger", "Trigger", true,
            "错误触发器：当其它工作流执行失败（产生 WorkflowFailedEvent）时触发本工作流，将失败工作流 ID 与错误信息作为首条 DataItem 传入，常用于失败补偿/告警。",
            ["trigger", "error", "failure"],
            null,
            AiDefinitionHelpers.Example("失败触发",
                JsonNode.Parse("""{"workflowId":"*"}"""),
                JsonNode.Parse("""{"workflowId":"...","errorMessage":"...","triggeredAt":"..."}""")));

    /// <summary>
    /// 被监控的工作流 ID；"*" 或留空表示监控任意工作流。
    /// </summary>
    [Description("被监控的工作流 ID；\"*\" 或留空表示监控任意工作流。")]
    public string? WorkflowId { get; set; }

    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            var batch = input.InputBatch;
            var payload = new JsonObject();

            string? workflowId = null;

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

                    // 优先从输入负载取 workflowId（失败工作流 ID）。
                    if (obj["workflowId"] is JsonNode wfNode)
                    {
                        workflowId = wfNode.ToString();
                    }
                }
                // 标量/数组/null 等其他类型：payload 保持为空（仅补充 triggeredAt）。
            }

            // 输入未携带 workflowId 时，回退到节点参数配置。
            workflowId ??= GetNodeParameter("workflowId") ?? WorkflowId;

            // 仅在输入未自带触发时间时补充（避免覆盖调用方显式提供的值）。
            if (!payload.ContainsKey("triggeredAt"))
            {
                payload["triggeredAt"] = DateTime.UtcNow.ToString("o");
            }

            // 仅记录触发动作与失败工作流 ID；不记录 errorMessage 原文，避免敏感/冗长内容落入日志。
            Logger?.LogInformation("errorTrigger 触发，失败工作流 {WorkflowId}。", workflowId);

            return Task.FromResult(NodeHandlerOutput.Data(new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = payload,
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            }));
        }
        catch (Exception ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, ex.Message);
        }
    }

    /// <summary>
    /// 从节点定义参数中按名称（大小写不敏感）读取字符串值。
    /// </summary>
    private string? GetNodeParameter(string key)
    {
        var node = ExecutionContext.Node;
        if (node is null)
        {
            return null;
        }

        foreach (var kv in node.Parameters)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase) && kv.Value is string s)
            {
                return s;
            }
        }

        return null;
    }
}
