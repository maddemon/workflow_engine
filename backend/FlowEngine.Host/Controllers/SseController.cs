using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FlowEngine.Application.Authorization;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Host.WebSocketHandlers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Net.ServerSentEvents;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// SSE（Server-Sent Events）兜底推送端点，当 WebSocket 不可用（如反向代理不支持）时
/// 通过 HTTP 长连接向客户端推送执行进度事件。
/// 使用 .NET 10 内置 <see cref="SseItem{T}"/> 和 <see cref="ServerSentEvents"/> 结果类型，
/// 自动处理 Content-Type、SSE 格式化和 Flush。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public class SseController(
    IEventBus eventBus,
    IAuthorizationGuard authGuard,
    ILogger<SseController> logger) : ControllerBase
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    /// <summary>
    /// 连接内事件序号计数器（每个 SSE 连接独立，从 0 开始单调递增）。
    /// 与 WebSocket 全局计数器（WebSocketEventPushService._sequenceCounter）不同，
    /// SSE 当前未实现断线重连补偿，序号仅用于单连接内消息排序。
    /// </summary>
    private long _sequenceCounter;

    /// <summary>
    /// 订阅指定执行的事件流（SSE）。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="cancellationToken">客户端断开时触发的取消令牌。</param>
    [HttpGet("executions/{executionId:guid}/stream")]
    [AuthorizePermission(Scope.Execution, Operation.Read)]
    public async Task<IResult> Stream(Guid executionId, CancellationToken cancellationToken)
    {
        await authGuard.RequireAccessAsync(ResourceKind.Execution, executionId, Operation.Read, cancellationToken);

        // 禁用 nginx 反向代理缓冲，确保事件实时推送
        Response.Headers["X-Accel-Buffering"] = "no";

        var channel = Channel.CreateUnbounded<WebSocketPushMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var subscriptions = SubscribeExecutionEvents(executionId, channel.Writer).ToList();

        return TypedResults.ServerSentEvents(
            StreamEventsAsync(channel, subscriptions, executionId, cancellationToken));
    }

    /// <summary>
    /// 将通道中的消息以 <see cref="SseItem{T}"/> 形式异步枚举，供 SSE 结果类型消费。
    /// 负责心跳推送、连接确认、订阅释放和通道关闭。
    /// </summary>
    private async IAsyncEnumerable<SseItem<WebSocketPushMessage>> StreamEventsAsync(
        Channel<WebSocketPushMessage> channel,
        List<IDisposable> subscriptions,
        Guid executionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CancellationTokenSource? heartbeatCts = null;

        try
        {
            logger.LogInformation("SSE connection established for execution {ExecutionId}", executionId);

            // 立即推送一次 connected 事件，确认连接已建立
            yield return new SseItem<WebSocketPushMessage>(
                new WebSocketPushMessage
                {
                    Type = "connected",
                    ExecutionId = executionId,
                    Timestamp = DateTime.UtcNow,
                    Sequence = Interlocked.Increment(ref _sequenceCounter),
                },
                eventType: "connected");

            heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = RunHeartbeatAsync(channel.Writer, heartbeatCts.Token, executionId);

            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // 为 SSE 推送的消息补充 Sequence 字段，与 WebSocket 推送保持一致
                var messageWithSequence = message with
                {
                    Sequence = Interlocked.Increment(ref _sequenceCounter)
                };
                yield return new SseItem<WebSocketPushMessage>(messageWithSequence, eventType: message.Type);
            }
        }
        finally
        {
            heartbeatCts?.Cancel();
            foreach (var sub in subscriptions)
            {
                sub?.Dispose();
            }
            channel.Writer.TryComplete();
            logger.LogInformation("SSE connection closed for execution {ExecutionId}", executionId);
        }
    }

    /// <summary>
    /// 订阅与指定执行相关的事件，过滤非本执行的事件后写入通道。
    /// A5：事件 → 映射工厂 表驱动订阅，消除 8 段重复的「判断归属 + 写入」样板。
    /// </summary>
    private IEnumerable<IDisposable> SubscribeExecutionEvents(Guid executionId, ChannelWriter<WebSocketPushMessage> writer)
    {
        yield return SubscribeOne(executionId, writer, (WorkflowStartedEvent evt) => evt.ExecutionId, evt => new WebSocketPushMessage
        {
            Type = "execution_started",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Payload = new
            {
                workflowDefinitionId = evt.WorkflowDefinitionId,
                eventType = evt.EventType,
            },
        });
        yield return SubscribeOne(executionId, writer, (NodeStartedEvent evt) => evt.ExecutionId, evt => new WebSocketPushMessage
        {
            Type = "node_started",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Payload = new
            {
                nodeDefinitionId = evt.NodeDefinitionId,
                runIndex = evt.RunIndex,
                eventType = evt.EventType,
            },
        });
        yield return SubscribeOne(executionId, writer, (NodeExecutedEvent evt) => evt.ExecutionId, evt => new WebSocketPushMessage
        {
            Type = "node_executed",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Payload = BuildNodeExecutedPayload(evt),
        });
        yield return SubscribeOne(executionId, writer, (NodeErrorEvent evt) => evt.ExecutionId, evt =>
        {
            var safeError = NodeErrorFactory.ToClientSafe(evt.Error);
            return new WebSocketPushMessage
            {
                Type = "node_error",
                ExecutionId = evt.ExecutionId,
                Timestamp = evt.OccurredAt,
                Payload = new
                {
                    nodeDefinitionId = evt.NodeDefinitionId,
                    runIndex = evt.RunIndex,
                    error = new
                    {
                        code = safeError.Code,
                        message = safeError.Message,
                    },
                    eventType = evt.EventType,
                },
            };
        });
        yield return SubscribeOne(executionId, writer, (WorkflowCompletedEvent evt) => evt.ExecutionId, evt => new WebSocketPushMessage
        {
            Type = "execution_completed",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Payload = new
            {
                workflowDefinitionId = evt.WorkflowDefinitionId,
                finalStatus = evt.FinalStatus.ToString(),
                eventType = evt.EventType,
            },
        });
        yield return SubscribeOne(executionId, writer, (WorkflowFailedEvent evt) => evt.ExecutionId, evt =>
        {
            var safeError = NodeErrorFactory.ToClientSafe(evt.Error);
            return new WebSocketPushMessage
            {
                Type = "execution_failed",
                ExecutionId = evt.ExecutionId,
                Timestamp = evt.OccurredAt,
                Payload = new
                {
                    workflowDefinitionId = evt.WorkflowDefinitionId,
                    error = new
                    {
                        code = safeError.Code,
                        message = safeError.Message,
                    },
                    eventType = evt.EventType,
                },
            };
        });
        yield return SubscribeOne(executionId, writer, (WorkflowCancelledEvent evt) => evt.ExecutionId, evt => new WebSocketPushMessage
        {
            Type = "execution_cancelled",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Payload = new
            {
                workflowDefinitionId = evt.WorkflowDefinitionId,
                eventType = evt.EventType,
            },
        });
        yield return SubscribeOne(executionId, writer, (LlmTokenStreamEvent evt) => evt.ExecutionId, evt => new WebSocketPushMessage
        {
            Type = "llm_token",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Payload = new
            {
                nodeDefinitionId = evt.NodeDefinitionId,
                runIndex = evt.RunIndex,
                delta = evt.Delta,
                isFinal = evt.IsFinal,
                eventType = evt.EventType,
            },
        });
    }

    /// <summary>
    /// 通用订阅辅助：仅当事件归属指定执行时，经映射工厂构建消息并写入通道（A5）。
    /// </summary>
    private IDisposable SubscribeOne<T>(
        Guid executionId,
        ChannelWriter<WebSocketPushMessage> writer,
        Func<T, Guid> executionIdSelector,
        Func<T, WebSocketPushMessage> mapper) where T : IDomainEvent
    {
        return eventBus.Subscribe<T>((evt, _) =>
        {
            if (executionIdSelector(evt) != executionId)
            {
                return Task.CompletedTask;
            }

            writer.TryWrite(mapper(evt));
            return Task.CompletedTask;
        });
    }

    private static object BuildNodeExecutedPayload(NodeExecutedEvent evt)
    {
        var result = evt.Result;
        var outputSummary = new
        {
            success = result.Success,
            itemCount = result.Output.Items.Count,
            error = result.Error is not null
                ? new { code = result.Error.Code, message = result.Error.Message }
                : null,
        };

        return new
        {
            nodeDefinitionId = evt.NodeDefinitionId,
            runIndex = evt.RunIndex,
            result = outputSummary,
            eventType = evt.EventType,
        };
    }

    /// <summary>
    /// 周期性向通道写入心跳事件，保持 SSE 连接活跃。
    /// </summary>
    private async Task RunHeartbeatAsync(
        ChannelWriter<WebSocketPushMessage> writer,
        CancellationToken cancellationToken,
        Guid executionId)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                writer.TryWrite(new WebSocketPushMessage
                {
                    Type = "heartbeat",
                    Timestamp = DateTime.UtcNow,
                    Sequence = Interlocked.Increment(ref _sequenceCounter),
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
            // 通道已关闭，正常退出
        }
        catch (Exception ex)
        {
            // 心跳循环中的非预期异常（如底层写入故障）不得静默忽略，
            // 记录日志以便观测，避免连接泄漏或心跳停摆而无从排查。
            logger.LogError(ex, "SSE 心跳任务异常，连接 {ExecutionId} 心跳可能已停止", executionId);
        }
    }
}
