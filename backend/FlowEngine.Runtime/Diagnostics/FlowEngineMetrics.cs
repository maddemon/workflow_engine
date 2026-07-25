using System.Diagnostics.Metrics;

namespace FlowEngine.Runtime.Diagnostics;

/// <summary>
/// FlowEngine 统一指标源（O-6 / OBS-7）。集中暴露执行、节点、失败与 WebSocket 广播计数，
/// 供 Prometheus 等采集端点抓取。所有计数均为单调计数器（只增不减）。
/// </summary>
public static class FlowEngineMetrics
{
    /// <summary>指标源名称。</summary>
    public const string MeterName = "FlowEngine";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>工作流执行启动计数。</summary>
    public static readonly Counter<int> ExecutionsStarted = Meter.CreateCounter<int>(
        "flowengine.executions.started", "次数", "已启动的工作流执行数");

    /// <summary>节点执行完成计数。</summary>
    public static readonly Counter<int> NodesExecuted = Meter.CreateCounter<int>(
        "flowengine.nodes.executed", "次数", "已完成的节点执行数");

    /// <summary>节点/执行失败计数。</summary>
    public static readonly Counter<int> Failures = Meter.CreateCounter<int>(
        "flowengine.failures", "次数", "节点或执行失败次数");

    /// <summary>WebSocket 广播成功计数。</summary>
    public static readonly Counter<int> WebSocketBroadcastSuccess = Meter.CreateCounter<int>(
        "flowengine.websocket.broadcast.success", "次数", "WebSocket 消息广播成功次数");

    /// <summary>WebSocket 广播失败计数。</summary>
    public static readonly Counter<int> WebSocketBroadcastFailure = Meter.CreateCounter<int>(
        "flowengine.websocket.broadcast.failure", "次数", "WebSocket 消息广播失败次数");
}
