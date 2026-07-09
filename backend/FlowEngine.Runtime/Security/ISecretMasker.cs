using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Security;

/// <summary>
/// 敏感数据脱敏器。将凭据值与已知敏感字面量替换为占位符，避免明文落库或泄露到审计/日志。
/// </summary>
public interface ISecretMasker
{
    /// <summary>脱敏数据批次。</summary>
    DataBatch MaskDataBatch(DataBatch batch, IReadOnlySet<string> sensitiveValues);

    /// <summary>脱敏节点执行结果。</summary>
    NodeExecutionResult MaskOutput(NodeExecutionResult output, IReadOnlySet<string> sensitiveValues);

    /// <summary>脱敏参数字典（含 <see cref="CredentialValue"/> 与敏感字面量）。</summary>
    Dictionary<string, object> MaskParameters(IReadOnlyDictionary<string, object> parameters, IReadOnlySet<string> sensitiveValues);

    /// <summary>脱敏单个值。</summary>
    object? MaskValue(object? value, IReadOnlySet<string> sensitiveValues);

    /// <summary>脱敏 JSON 节点树。</summary>
    JsonNode? MaskJsonNode(JsonNode? node, IReadOnlySet<string> sensitiveValues);
}
