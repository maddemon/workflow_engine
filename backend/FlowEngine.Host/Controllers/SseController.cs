using System.Text.Json;
using System.Threading.Channels;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Events;
using FlowEngine.Host.WebSocketHandlers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// SSE（Server-Sent Events）兜底推送端点，当 WebSocket 不可用（如反向代理不支持）时
/// 通过 HTTP 长连接向客户端推送执行进度事件。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public class SseController(
    IEventBus eventBus,
    IUserContext userContext,
    ILogger<SseController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 订阅指定执行的事件流（SSE）。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="cancellationToken">客户端断开时触发的取消令牌。</param>
    [HttpGet("executions/{executionId:guid}/stream")]
    [AuthorizePermission(Scope.Execution, Operation.Read)]
    public async Task Stream(Guid executionId, CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        // 禁用 nginx 反向代理缓冲，确保事件实时推送
        Response.Headers["X-Accel-Buffering"] = "no";

        var channel = Channel.CreateUnbounded<WebSocketPushMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var subscriptions = SubscribeExecutionEvents(executionId, channel.Writer);
        CancellationTokenSource? heartbeatCts = null;

        try
        {
            logger.LogInformation("SSE connection established for execution {ExecutionId}", executionId);

            // 立即推送一次 connected 事件，确认连接已建立
            await WriteSseAsync(new WebSocketPushMessage
            {
                Type = "connected",
                ExecutionId = executionId,
                Timestamp = DateTime.UtcNow,
            }, cancellationToken).ConfigureAwait(false);

            heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var heartbeatTask = RunHeartbeatAsync(channel.Writer, heartbeatCts.Token);

            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await WriteSseAsync(message, cancellationToken).ConfigureAwait(false);
                await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await heartbeatTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 客户端断开连接，正常退出
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SSE stream error for execution {ExecutionId}", executionId);
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
    private List<IDisposable> SubscribeExecutionEvents(Guid executionId, ChannelWriter<WebSocketPushMessage> writer)
    {
        var subs = new List<IDisposable>();

        subs.Add(eventBus.Subscribe<WorkflowStartedEvent>((evt, _) =>
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
        }));

        subs.Add(eventBus.Subscribe<NodeExecutedEvent>((evt, _) =>
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
        }));

        subs.Add(eventBus.Subscribe<NodeErrorEvent>((evt, _) =>
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
        }));

        subs.Add(eventBus.Subscribe<WorkflowCompletedEvent>((evt, _) =>
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
        }));

        subs.Add(eventBus.Subscribe<WorkflowFailedEvent>((evt, _) =>
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
        }));

        subs.Add(eventBus.Subscribe<WorkflowCancelledEvent>((evt, _) =>
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
        }));

        subs.Add(eventBus.Subscribe<LlmTokenStreamEvent>((evt, _) =>
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
        }));

        return subs;
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

    /// <summary>
    /// 将消息以 SSE 格式写入响应流。
    /// </summary>
    private async Task WriteSseAsync(WebSocketPushMessage message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken).ConfigureAwait(false);
    }
}
