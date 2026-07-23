using System.Text.Json;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Host.Triggers;

/// <summary>
/// 订阅 <see cref="WorkflowFailedEvent"/>：当某工作流失败时，查找包含 errorTrigger 节点、
/// 且其 <c>WorkflowId</c> 参数匹配失败工作流（或 <c>"*"</c>/空 表示监控任意工作流）的激活工作流，
/// 通过 <see cref="IEngine.StartAsync"/> 触发它们（payload 携带失败工作流 ID 与错误信息）。
/// <para>本类只交付"失败事件 → 启动匹配工作流"的接线；errorTrigger 节点本身由任务 N17 实现。</para>
/// </summary>
public sealed class ErrorTriggerEventConsumer(
    IServiceProvider serviceProvider,
    ILogger<ErrorTriggerEventConsumer> logger) : INotificationHandler<WorkflowFailedEvent>
{
    /// <inheritdoc />
    public async Task Handle(WorkflowFailedEvent notification, CancellationToken cancellationToken)
    {
        var failedWorkflowId = notification.WorkflowDefinitionId;
        var errorMessage = notification.Error?.Message;

        // 每个事件在独立作用域解析 scoped 的 IEngine 与 DbContext，避免捕获失败执行的作用域
        // （与 WorkflowExecutionWorker 每执行项独立 scope 的方案一致，规避 Scoped 依赖被长生命周期捕获）。
        await using var scope = serviceProvider.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IEngine>();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        // 仅激活工作流可被 errorTrigger 触发；过滤后于内存中匹配 errorTrigger 节点。
        var candidates = await dbContext.Workflows
            .AsNoTracking()
            .Where(w => w.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var workflow in candidates)
        {
            // 防自环：error 工作流自身失败时不触发自身，避免无限循环。
            if (workflow.Id == failedWorkflowId)
            {
                continue;
            }

            if (!HasMatchingErrorTrigger(workflow, failedWorkflowId))
            {
                continue;
            }

            var payload = new { workflowId = failedWorkflowId, errorMessage = errorMessage };
            try
            {
                await engine.StartAsync(workflow.Id, payload, cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "errorTrigger 已为失败工作流 {FailedWorkflowId} 启动工作流 {WorkflowId}。",
                    failedWorkflowId, workflow.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "errorTrigger 启动工作流 {WorkflowId}（失败工作流 {FailedWorkflowId}）失败。",
                    workflow.Id, failedWorkflowId);
            }
        }
    }

    private static bool HasMatchingErrorTrigger(Workflow workflow, Guid failedWorkflowId)
    {
        foreach (var node in workflow.Nodes)
        {
            if (!string.Equals(node.TypeName, "errorTrigger", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var monitored = GetStringParameter(node, "WorkflowId");

            // 未配置，或 "*"/空：监控任意工作流。
            if (string.IsNullOrWhiteSpace(monitored) || monitored == "*")
            {
                return true;
            }

            if (Guid.TryParse(monitored, out var monitoredId) && monitoredId == failedWorkflowId)
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetStringParameter(NodeDefinition node, string key)
    {
        // NodeDefinition.Parameters 以参数描述符名（camelCase）为键，但调用方传入的 key 可能为
        // PascalCase（如 "WorkflowId"）。生产环境参数值实际存于 "workflowId"（见 ParameterDiscoverer.ToCamelCase），
        // 故此处做大小写不敏感查找，兼容两种写法，修复生产路径匹配失败。
        if (!TryGetValueCaseInsensitive(node.Parameters, key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            string s => s,
            JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
            _ => raw.ToString()
        };
    }

    private static bool TryGetValueCaseInsensitive(
        IReadOnlyDictionary<string, object> parameters, string key, out object? value)
    {
        foreach (var kv in parameters)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
