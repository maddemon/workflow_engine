using System.Collections.Generic;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;
/// <summary>节点输入——精简视图，不含凭据/日志/基础设施。已求值参数经节点自身属性的 Script.ResolvedValue 读取（见 NodeBase.GetResolved）。
/// 不提供 Required&lt;T&gt;/GetParameter 等“向 context 索取”的方法；失败用抛 <see cref="NodeExecutionException"/>。</summary>
public sealed class NodeInput
{
    /// <summary>当前输入数据批次。</summary>
    public DataBatch InputBatch { get; }

    /// <summary>全局变量（可能为共享字典，不区分大小写由写入方决定）。</summary>
    public IReadOnlyDictionary<string, object?> Globals { get; }

    /// <summary>当前逐项执行索引（来自上下文 RunIndex），非逐项执行时为 null。</summary>
    public int? ItemIndex { get; }

    /// <summary>全部端口输入批次，按端口名键控（含多端口节点各输入端口的数据）。</summary>
    public IReadOnlyDictionary<string, DataBatch> AllInputs { get; }

    /// <summary>构造节点输入。</summary>
    /// <param name="inputBatch">输入批次。</param>
    /// <param name="globals">全局变量，可空。</param>
    /// <param name="itemIndex">逐项索引，可空。</param>
    /// <param name="allInputs">全部端口输入批次，可空；缺省为空字典。</param>
    public NodeInput(DataBatch inputBatch, IReadOnlyDictionary<string, object?>? globals = null, int? itemIndex = null, IReadOnlyDictionary<string, DataBatch>? allInputs = null)
        => (InputBatch, Globals, ItemIndex, AllInputs) = (inputBatch, globals ?? new Dictionary<string, object?>(), itemIndex, allInputs ?? new Dictionary<string, DataBatch>());
}
