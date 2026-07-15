using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Quartz;

namespace FlowEngine.Host.Jobs;

/// <summary>
/// Quartz 轮询触发器 Job，定期查询外部系统并触发工作流执行。
/// </summary>
public sealed class PollTriggerJob(
    IEngine engine,
    FlowEngineDbContext dbContext,
    INodeRegistry nodeRegistry,
    IMemoryCache cache,
    IExecutionIdempotencyService idempotencyService,
    ILogger<PollTriggerJob> logger,
    IEventBus eventBus,
    AuditEventFactory auditFactory) : IJob
{
    /// <summary>
    /// JobDataMap 中触发器 ID 的键。
    /// </summary>
    public const string TriggerIdKey = "TriggerId";

    /// <summary>
    /// JobDataMap 中工作流定义 ID 的键。
    /// </summary>
    public const string WorkflowDefinitionIdKey = "WorkflowDefinitionId";

    /// <summary>
    /// 标记当前是否正在执行。使用 ConcurrentDictionary 替代 HashSet 以保证线程安全（Code Review I-3）。
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, byte> _runningJobs = new();

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        var dataMap = context.MergedJobDataMap;
        var triggerId = dataMap.GetGuid(TriggerIdKey);
        var workflowDefinitionId = dataMap.GetGuid(WorkflowDefinitionIdKey);

        logger.LogInformation(
            "轮询触发器执行: TriggerId={TriggerId}, WorkflowDefinitionId={WorkflowDefinitionId}",
            triggerId, workflowDefinitionId);

        var trigger = await dbContext.Triggers.FirstOrDefaultAsync(t => t.Id == triggerId, context.CancellationToken)
            .ConfigureAwait(false);

        if (trigger is null || !trigger.IsActive)
        {
            logger.LogWarning("轮询触发器不存在或已停用: TriggerId={TriggerId}", triggerId);
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.PollSkipped,
                "Trigger",
                triggerId,
                new Dictionary<string, object> { ["reason"] = "inactive" }),
                context.CancellationToken).ConfigureAwait(false);
            return;
        }

        var settings = trigger.Settings;
        if (settings is null || string.IsNullOrEmpty(settings.PollNodeId))
        {
            logger.LogWarning("轮询触发器缺少配置: TriggerId={TriggerId}", triggerId);
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.PollSkipped,
                "Trigger",
                triggerId,
                new Dictionary<string, object> { ["reason"] = "missing_settings" }),
                context.CancellationToken).ConfigureAwait(false);
            return;
        }

        // 检查 SkipIfRunning
        if (settings.SkipIfRunning && !_runningJobs.TryAdd(triggerId, 0))
        {
            logger.LogInformation(
                "轮询触发器跳过（上一次执行仍在运行）: TriggerId={TriggerId}",
                triggerId);
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.PollSkipped,
                "Trigger",
                triggerId,
                new Dictionary<string, object> { ["reason"] = "skip_if_running" }),
                context.CancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            // 获取节点类型
            if (!nodeRegistry.TryGet(settings.PollNodeId, out var nodeType) || nodeType is null)
            {
                logger.LogWarning(
                    "轮询触发器节点类型未注册: TriggerId={TriggerId}, PollNodeId={PollNodeId}",
                    triggerId, settings.PollNodeId);
                await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                    AuditEventTypes.PollSkipped,
                    "Trigger",
                    triggerId,
                    new Dictionary<string, object> { ["reason"] = "node_not_registered", ["pollNodeId"] = settings.PollNodeId }),
                    context.CancellationToken).ConfigureAwait(false);
                return;
            }

            // 创建节点执行上下文（简化版，用于轮询）
            var nodeExecutionContext = CreateNodeExecutionContext(settings, context.CancellationToken);

            // 执行节点以获取数据
            var executionResult = await nodeType.ExecuteAsync(nodeExecutionContext, context.CancellationToken)
                .ConfigureAwait(false);

            if (!executionResult.Success)
            {
                logger.LogWarning(
                    "轮询节点执行失败: TriggerId={TriggerId}, Error={Error}",
                    triggerId, executionResult.Error?.Message);
                await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                    AuditEventTypes.PollSkipped,
                    "Trigger",
                    triggerId,
                    new Dictionary<string, object> { ["reason"] = "node_failed", ["error"] = executionResult.Error?.Message ?? string.Empty }),
                    context.CancellationToken).ConfigureAwait(false);
                return;
            }

            // 处理数据项
            var outputItems = executionResult.Output?.Items ?? [];
            var newItems = new List<DataItem>();

            foreach (var item in outputItems)
            {
                if (PollDeduplication.ShouldProcess(item, settings.DedupStrategy, settings.LastPollId, settings.LastPollTime))
                {
                    newItems.Add(item);
                }
                else
                {
                    await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                        AuditEventTypes.PollSkipped,
                        "Trigger",
                        trigger.Id,
                        new Dictionary<string, object>
                        {
                            ["reason"] = "deduplication",
                            ["lastPollId"] = settings.LastPollId ?? string.Empty,
                        }),
                        context.CancellationToken).ConfigureAwait(false);
                }
            }

            if (newItems.Count > 0)
            {
                logger.LogInformation(
                    "轮询触发器发现 {Count} 条新数据: TriggerId={TriggerId}",
                    newItems.Count, triggerId);

                // 为每个新数据项触发工作流
                foreach (var item in newItems)
                {
                    try
                    {
                        var payload = item.Data?.ToJsonString() ?? "{}";
                        var idempotencyKey = ComputeIdempotencyKey(triggerId, payload);

                        // 幂等兜底：先查内存缓存（热缓存），再查数据库（权威源）
                        if (cache.TryGetValue(idempotencyKey, out _))
                        {
                            logger.LogInformation(
                                "轮询触发器跳过重复执行(缓存命中): TriggerId={TriggerId}, IdempotencyKey={IdempotencyKey}",
                                triggerId, idempotencyKey);
                            continue;
                        }

                        var ttlSeconds = settings.IdempotencyTtlSeconds ?? 3600;
                        var ttl = TimeSpan.FromSeconds(ttlSeconds);
                        var tempExecutionId = Guid.NewGuid();
                        var existingExecutionId = await idempotencyService.TryGetOrRegisterAsync(
                            idempotencyKey, tempExecutionId, ttl, context.CancellationToken).ConfigureAwait(false);

                        if (existingExecutionId.HasValue)
                        {
                            // 写入热缓存，后续请求可跳过数据库查询
                            cache.Set(idempotencyKey, true, TimeSpan.FromMinutes(5));
                            logger.LogInformation(
                                "轮询触发器跳过重复执行(DB命中): TriggerId={TriggerId}, IdempotencyKey={IdempotencyKey}",
                                triggerId, idempotencyKey);
                            continue;
                        }

                        var executionId = await engine.StartAsync(
                            workflowDefinitionId,
                            triggerPayload: new { triggerType = TriggerType.Poll.ToString(), triggerId, data = payload },
                            context.CancellationToken).ConfigureAwait(false);

                        // 写入热缓存
                        cache.Set(idempotencyKey, true, TimeSpan.FromMinutes(5));

                        // 更新幂等记录的 ExecutionId 为实际值
                        await UpdateIdempotencyExecutionIdAsync(idempotencyKey, executionId.Value, context.CancellationToken)
                            .ConfigureAwait(false);

                        logger.LogInformation(
                            "轮询触发器触发工作流成功: TriggerId={TriggerId}, ExecutionId={ExecutionId}",
                            triggerId, executionId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "轮询触发器触发工作流失败: TriggerId={TriggerId}",
                            triggerId);
                    }
                }

                // 更新去重状态
                var updatedSettings = PollDeduplication.UpdateState(newItems, settings);
                trigger.Settings = updatedSettings;
                trigger.LastTriggeredAt = DateTime.UtcNow;
                trigger.NextTriggerAt = context.Trigger.GetNextFireTimeUtc()?.UtcDateTime;
                trigger.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                logger.LogDebug(
                    "轮询触发器未发现新数据: TriggerId={TriggerId}",
                    triggerId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "轮询触发器执行异常: TriggerId={TriggerId}",
                triggerId);
        }
        finally
        {
            _runningJobs.TryRemove(triggerId, out _);
        }
    }

    private static NodeExecutionContext CreateNodeExecutionContext(TriggerSettings settings, CancellationToken cancellationToken)
    {
        // 创建一个简化的节点执行上下文用于轮询
        var nodeDefinition = new NodeDefinition
        {
            Id = "trigger",
            TypeName = settings.PollNodeId ?? string.Empty,
            Parameters = new Dictionary<string, object>(),
        };

        return new NodeExecutionContext
        {
            Workflow = new Workflow(),
            ExecutionId = Guid.NewGuid(),
            Node = nodeDefinition,
            RunIndex = 0,
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            CancellationToken = cancellationToken,
        };
    }

    /// <summary>
    /// 计算轮询执行的幂等键。
    /// </summary>
    /// <param name="triggerId">触发器 ID。</param>
    /// <param name="payload">数据负载。</param>
    /// <returns>幂等键。</returns>
    private static string ComputeIdempotencyKey(Guid triggerId, string payload)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"poll-exec:{triggerId}:{Convert.ToHexString(hashBytes, 0, 8)}";
    }

    /// <summary>
    /// 更新幂等记录的 ExecutionId 为实际值。
    /// </summary>
    private async Task UpdateIdempotencyExecutionIdAsync(
        string idempotencyKey,
        Guid actualExecutionId,
        CancellationToken ct)
    {
        var record = await dbContext.ExecutionDedups
            .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, ct)
            .ConfigureAwait(false);

        if (record is not null && record.ExecutionId != actualExecutionId)
        {
            record.ExecutionId = actualExecutionId;
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
