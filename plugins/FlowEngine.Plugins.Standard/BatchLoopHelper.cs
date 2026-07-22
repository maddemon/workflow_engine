using System.Collections.Generic;
using FlowEngine.Core.Entities;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 批循环共享 helper。将「缓存全集 / 推进 position / 取窗口」的迭代逻辑从 <see cref="LoopNode"/> 抽出，
/// 供 LoopNode 与未来的 batchSplit 等批迭代节点复用，避免逻辑重复。
/// <para>该 helper 仅操作节点级持久化上下文（<see cref="IDictionary{TKey,TValue}"/>）与一组约定键名，
/// 不依赖 <c>INodeType</c> / <c>NodeExecutionContext</c> 等公共契约，故本任务无需修改任何接口或内核。
/// 通用反馈边循环机制由 <c>WorkflowSchedulerKernel</c> 提供，节点本身只负责窗口切分与位置推进。</para>
/// </summary>
internal static class BatchLoopHelper
{
    /// <summary>初始化标记键。</summary>
    public const string KeyInitialized = "initialized";

    /// <summary>原始输入全集缓存键。</summary>
    public const string KeyAllItems = "allItems";

    /// <summary>当前迭代位置键。</summary>
    public const string KeyPosition = "position";

    /// <summary>已处理累积键（LoopNode 使用；batchSplit 可不使用）。</summary>
    public const string KeyProcessedItems = "processedItems";

    /// <summary>BranchIndex：Loop 输出口。</summary>
    public const int BranchLoop = 0;

    /// <summary>BranchIndex：Done 输出口。</summary>
    public const int BranchDone = 1;

    /// <summary>
    /// 读取 position，兼容 int / double：节点 body 表达式（Jint）写回字典的值统一为 double，
    /// 故需回退到 (int) 而非静默归零导致重新从首项迭代。
    /// </summary>
    public static int ReadPosition(IDictionary<string, object?> nodeContext)
    {
        var value = nodeContext.GetValue(KeyPosition);
        return value switch
        {
            int i => i,
            double d => (int)d,
            _ => 0
        };
    }

    /// <summary>
    /// 首次激活（或未初始化）时初始化迭代状态：缓存 allItems、position=0。
    /// 返回 <c>true</c> 表示本次为首次初始化（调用方据此决定是否立即发出首批窗口）；
    /// 返回 <c>false</c> 表示上下文已初始化（如回环激活复用旧上下文），调用方应继续累积反馈数据。
    /// </summary>
    public static bool EnsureInitialized(IDictionary<string, object?> nodeContext, IReadOnlyList<DataItem> allItems)
    {
        if (nodeContext.ContainsKey(KeyInitialized))
        {
            return false;
        }

        nodeContext[KeyInitialized] = true;
        nodeContext[KeyAllItems] = allItems.ToList();
        nodeContext[KeyPosition] = 0;
        return true;
    }

    /// <summary>
    /// 取当前窗口：position 未越过全集则从 Loop 输出口发出 [position, position+BatchSize) 切片，
    /// 并推进 position（步长取实际窗口大小，避免末批超调）；
    /// position 已越过全集则从 Done 输出口发出 <paramref name="donePayload"/>
    /// （LoopNode 传 processedItems 累积结果，batchSplit 可传空批次或计数）。
    /// </summary>
    /// <param name="nodeContext">节点级持久化上下文。</param>
    /// <param name="batchSize">单批最大项数（调用方须先钳制为 &gt;= 1）。</param>
    /// <param name="donePayload">position 越过全集时从 Done 输出口回吐的数据批次。</param>
    public static NodeExecutionResult EmitNextWindow(
        IDictionary<string, object?> nodeContext,
        int batchSize,
        DataBatch donePayload)
    {
        var position = ReadPosition(nodeContext);
        var storedItems = nodeContext.Get<List<DataItem>>(KeyAllItems) ?? [];

        // 全部处理完：走 Done 输出口，回吐 donePayload。
        if (position >= storedItems.Count)
        {
            return new NodeExecutionResult
            {
                Success = true,
                Output = donePayload,
                BranchIndex = BranchDone
            };
        }

        // 取当前窗口；推进步长取实际窗口大小（末批可能不足 batchSize），避免末批超调。
        var batchItems = storedItems.Skip(position).Take(batchSize).ToList();
        nodeContext[KeyPosition] = position + batchItems.Count;

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = batchItems },
            BranchIndex = BranchLoop
        };
    }
}
