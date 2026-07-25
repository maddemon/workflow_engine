using System.Collections;
using System.Linq;
using System.Text.Json;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.ValueObjects;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 调度内核共享的静态辅助方法，供多个协作者类（<see cref="RetryExecutor"/>、
/// <see cref="OutputRouter"/>、<see cref="NodeProcessor"/>、<see cref="TimeoutProcessor"/>）复用，
/// 避免各协作者重复持有相同逻辑、并通过单一来源保证行为一致。
/// </summary>
internal static class SchedulerHelpers
{
    /// <summary>
    /// 节点错误事件的安全包装：确保 <see cref="NodeErrorEvent"/> 始终携带非 null 的 <see cref="NodeError"/>，
    /// 避免失败节点缺少错误信息时抛出 NullReferenceException 而中断执行链路。
    /// </summary>
    /// <param name="error">可能为 null 的原始错误。</param>
    /// <returns>非 null 的节点错误。</returns>
    internal static NodeError SafeError(NodeError? error) =>
        error ?? new NodeError { Code = "NodeExecutionFailed", Message = "节点执行失败（无详细错误）" };

    /// <summary>
    /// 将触发负载转换为 <see cref="DataBatch"/>：优先原样透传 <see cref="DataBatch"/> / <see cref="DataItem"/>，
    /// null 视为单条空数据项，可枚举对象逐项序列化，单值序列化为单条数据项。
    /// 供入口节点入队（<see cref="WorkflowSchedulerKernel.EnqueueEntryNodesAsync"/>）与
    /// <see cref="NodeProcessor"/> 复用，确保负载转换逻辑单一来源。
    /// </summary>
    /// <param name="payload">触发负载。</param>
    /// <returns>转换后的数据批。</returns>
    internal static DataBatch CreateDataBatch(object? payload)
    {
        if (payload is DataBatch batch) return batch;
        if (payload is DataItem item) return new DataBatch { Items = [item] };

        if (payload is null)
        {
            return new DataBatch
            {
                Items =
                [
                    new DataItem { Data = null, Success = true, SourceIndex = 0 }
                ]
            };
        }

        if (payload is IEnumerable enumerable && payload is not string)
        {
            var items = new List<DataItem>();
            var index = 0;
            foreach (var value in enumerable)
            {
                items.Add(new DataItem
                {
                    Data = JsonSerializer.SerializeToNode(value, JsonDefaults.Options),
                    Success = true,
                    SourceIndex = index++
                });
            }
            return new DataBatch { Items = items };
        }

        var data = JsonSerializer.SerializeToNode(payload, JsonDefaults.Options);
        return new DataBatch
        {
            Items =
            [
                new DataItem { Data = data, Success = true, SourceIndex = 0 }
            ]
        };
    }
}
