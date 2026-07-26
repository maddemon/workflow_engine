using System.Threading;
using System.Threading.Tasks;

namespace FlowEngine.Runtime.Execution.Pipeline;

/// <summary>管线阶段契约。每个阶段处理一类横切关注点（校验/求值/执行/路由/持久化等）。
/// 通过 <paramref name="next"/> 显式驱动后续阶段；若阶段设置 <c>context.Result</c> 且不再调用 next，
/// 驱动器将跳过中间阶段、直达末端持久化阶段（短路）。不套用 ASP.NET 中间件抽象。</summary>
public interface IExecutionStage
{
    /// <summary>执行本阶段。需继续时调用 next；需短路时不调用 next 并设置 context.Result。</summary>
    /// <param name="context">跨阶段共享的管线上下文。</param>
    /// <param name="next">驱动后续阶段的委托；调用即继续，不调用即短路。</param>
    /// <param name="ct">取消令牌。</param>
    Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct);
}
