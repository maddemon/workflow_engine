using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
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
    IUserContext userContext,
    IResourceAuthorizationService resourceAuthorization,
    ILogger<SseController> logger) : ControllerBase
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 订阅指定执行的事件流（SSE）。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="cancellationToken">客户端断开时触发的取消令牌。</param>
    [HttpGet("executions/{executionId:guid}/stream")]
    [AuthorizePermission(Scope.Execution, Operation.Read)]
    public async Task<IResult> Stream(Guid executionId, CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return TypedResults.StatusCode(StatusCodes.Status401Unauthorized);
        }

        if (userContext.UserId is not { } userId ||
            !await resourceAuthorization.CanAccessExecutionAsync(userId, executionId, Operation.Read, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

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
                },
                eventType: "connected");

            heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = RunHeartbeatAsync(channel.Writer, heartbeatCts.Token);

            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return new SseItem<WebSocketPushMessage>(message, eventType: message.Type);
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
    /// </summary>
    private IEnumerable<IDisposable> SubscribeExecutionEvents(Guid executionId, ChannelWriter<WebSocketPushMessage> writer)
    {
        yield return eventBus.Subscribe<WorkflowStartedEvent>((evt, _) =>
        {
            if (evt.ExecutionId == executionId)
            {
                writer.TryWrite(new WebSocketPushMessage
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
            }
            return Task.CompletedTask;
        });
        yield return eventBus.Subscribe<NodeStartedEvent>((evt, _) =>
        {
            if (evt.ExecutionId == executionId)
            {
                writer.TryWrite(new WebSocketPushMessage
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
            }
            return Task.CompletedTask;
        });
        yield return eventBus.Subscribe<NodeExecutedEvent>((evt, _) =>
        {
            if (evt.ExecutionId == executionId)
            {
                writer.TryWrite(new WebSocketPushMessage
                {
                    Type = "node_executed",
                    ExecutionId = evt.ExecutionId,
                    Timestamp = evt.OccurredAt,
                    Payload = BuildNodeExecutedPayload(evt),
                });
            }
            return Task.CompletedTask;
        });
        yield return eventBus.Subscribe<NodeErrorEvent>((evt, _) =>
        {
            if (evt.ExecutionId == executionId)
            {
                writer.TryWrite(new WebSocketPushMessage
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
                            code = evt.Error.Code,
                            message = evt.Error.Message,
                        },
                        eventType = evt.EventType,
                    },
                });
            }
            return Task.CompletedTask;
        });
        yield return eventBus.Subscribe<WorkflowCompletedEvent>((evt, _) =>
        {
            if (evt.ExecutionId == executionId)
            {
                writer.TryWrite(new WebSocketPushMessage
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
            }
            return Task.CompletedTask;
        });
        yield return eventBus.Subscribe<WorkflowFailedEvent>((evt, _) =>
        {
            if (evt.ExecutionId == executionId)
            {
                writer.TryWrite(new WebSocketPushMessage
                {
                    Type = "execution_failed",
                    ExecutionId = evt.ExecutionId,
                    Timestamp = evt.OccurredAt,
                    Payload = new
                    {
                        workflowDefinitionId = evt.WorkflowDefinitionId,
                        error = new
                        {
                            code = evt.Error.Code,
                            message = evt.Error.Message,
                        },
                        eventType = evt.EventType,
                    },
                });
            }
            return Task.CompletedTask;
        });
        yield return eventBus.Subscribe<WorkflowCancelledEvent>((evt, _) =>
        {
            if (evt.ExecutionId == executionId)
            {
                writer.TryWrite(new WebSocketPushMessage
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
            }
            return Task.CompletedTask;
        });
        yield return eventBus.Subscribe<LlmTokenStreamEvent>((evt, _) =>
        {
            if (evt.ExecutionId == executionId)
            {
                writer.TryWrite(new WebSocketPushMessage
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
    private async Task RunHeartbeatAsync(ChannelWriter<WebSocketPushMessage> writer, CancellationToken cancellationToken)
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
    }
}
