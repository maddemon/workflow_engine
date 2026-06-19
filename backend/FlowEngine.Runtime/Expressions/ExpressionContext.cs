using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 表达式求值上下文。
/// </summary>
public sealed class ExpressionContext
{
    /// <summary>
    /// 按端口名组织的输入数据批次。
    /// </summary>
    public IReadOnlyDictionary<string, DataBatch> Inputs { get; init; } =
        new Dictionary<string, DataBatch>();

    /// <summary>
    /// 节点参数原始值。
    /// </summary>
    public IReadOnlyDictionary<string, object> RawParameters { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 上游节点输出，按节点名组织。
    /// </summary>
    public IReadOnlyDictionary<string, DataBatch> NodeOutputs { get; init; } =
        new Dictionary<string, DataBatch>();

    /// <summary>
    /// 上游节点完整批次，按节点名组织。
    /// </summary>
    public IReadOnlyDictionary<string, DataBatch> NodeBatches { get; init; } =
        new Dictionary<string, DataBatch>();

    /// <summary>
    /// 允许访问的环境变量白名单。
    /// </summary>
    public IReadOnlySet<string> EnvironmentWhitelist { get; init; } =
        new HashSet<string>();

    /// <summary>
    /// 工作流与执行元数据。
    /// </summary>
    public ExpressionMetadata Metadata { get; init; } = new();

    /// <summary>
    /// 获取指定输入端口的当前数据项，按 <see cref="ExpressionMetadata.RunIndex"/> 取值。
    /// </summary>
    /// <param name="portName">端口名称，默认为 "input"。</param>
    /// <returns>当前数据项的数据对象，不存在时返回 null。</returns>
    internal object? GetCurrentItem(string portName = "input")
    {
        if (!Inputs.TryGetValue(portName, out var batch) || batch.Items.Count == 0)
        {
            return null;
        }

        var index = Metadata.RunIndex >= 0 && Metadata.RunIndex < batch.Items.Count
            ? Metadata.RunIndex
            : 0;
        var item = batch.Items[index];
        return item.Data is JsonObject jsonObject
            ? jsonObject
            : item.Data;
    }
}
