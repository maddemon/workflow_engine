using FlowEngine.Core.Abstractions;
using System.Text.Json;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点执行上下文，承载单次节点执行所需的运行时数据与服务。
/// </summary>
public class NodeExecutionContext
{
    /// <summary>
    /// 所属工作流。
    /// </summary>
    public Workflow Workflow { get; set; } = new();

    /// <summary>
    /// 执行 ID。
    /// </summary>
    public Guid ExecutionId { get; set; }

    /// <summary>
    /// 当前节点定义。
    /// </summary>
    public NodeDefinition Node { get; set; } = new();

    /// <summary>
    /// 运行索引。
    /// </summary>
    public int RunIndex { get; set; }

    /// <summary>
    /// 输入数据批次映射。
    /// </summary>
    public IReadOnlyDictionary<string, DataBatch> Inputs { get; set; } = new Dictionary<string, DataBatch>();

    /// <summary>
    /// 原始参数映射（未经表达式求值）。
    /// </summary>
    public IReadOnlyDictionary<string, object> RawParameters { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// 解析后的参数映射（表达式已求值）。
    /// </summary>
    public IReadOnlyDictionary<string, object> ResolvedParameters { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// 凭据访问器。
    /// </summary>
    public ICredentialAccessor Credentials { get; set; } = null!;

    /// <summary>
    /// 执行日志记录器。
    /// </summary>
    public IExecutionLogger Logger { get; set; } = null!;

    /// <summary>
    /// 取消令牌。
    /// </summary>
    public CancellationToken CancellationToken { get; set; }


    public T? GetParameter<T>(string name) where T : class
    {
        if (ResolvedParameters.TryGetValue(name, out var value) && value is T typed)
        {
            return typed;
        }

        if (RawParameters.TryGetValue(name, out var rawValue) && rawValue is T rawTyped)
        {
            return rawTyped;
        }

        return null;
    }

    public NodeExecutionResult ErrorResult(string code, string message)
    {
        return new NodeExecutionResult
        {
            Success = false,
            Error = new NodeError
            {
                Code = code,
                Message = message,
                NodeDefinitionId = Node.Id
            }
        };
    }

    public object? InputData
    {
        get
        {
            if (!Inputs.TryGetValue("input", out var batch) || batch.Items.Count == 0)
            {
                return null;
            }

            var firstItem = batch.Items[0];
            if (firstItem.Data is null) return null;

            // 将 JsonNode 序列化为 JSON 字符串后反序列化为 .NET 原生类型，供 Jint 使用
            var json = firstItem.Data.ToJsonString();
            return JsonSerializer.Deserialize<object>(json);
        }
    }
}
