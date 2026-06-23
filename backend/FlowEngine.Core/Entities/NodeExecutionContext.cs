using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;

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

    /// <summary>
    /// LLM 客户端，供 Agent 等节点调用大语言模型。
    /// </summary>
    public ILlmClient? LlmClient { get; set; }

    /// <summary>
    /// 节点注册中心，供 Agent 等节点查找下游节点类型。
    /// </summary>
    public INodeRegistry? NodeRegistry { get; set; }

    /// <summary>
    /// 获取参数值，优先从 ResolvedParameters 获取，其次从 RawParameters 获取。
    /// </summary>
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

    /// <summary>
    /// 创建错误结果。
    /// </summary>
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

    /// <summary>
    /// 获取输入数据（供 Jint 使用）。
    /// </summary>
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

            var json = firstItem.Data.ToJsonString();
            return JsonSerializer.Deserialize<object>(json);
        }
    }

    /// <summary>
    /// 从输入端口获取 JsonNode 数据。
    /// </summary>
    public JsonNode? GetInputPayload()
    {
        if (Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) && batch.Items.Count > 0)
        {
            return batch.Items[0].Data;
        }

        return null;
    }

    /// <summary>
    /// 解析凭据并返回 API Key。
    /// </summary>
    public async Task<string?> ResolveApiKeyAsync(string? credentialId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(credentialId))
        {
            return null;
        }

        if (!Guid.TryParse(credentialId, out var id))
        {
            return null;
        }

        try
        {
            var credential = await Credentials.GetCredentialAsync(id, cancellationToken)
                .ConfigureAwait(false);

            if (credential.Fields.TryGetValue(FlowConstants.CredentialFields.ApiKey, out var apiKey))
            {
                return apiKey;
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to resolve credential {CredentialId}.", credentialId);
            return null;
        }
    }

    /// <summary>
    /// 创建单个数据项的结果。
    /// </summary>
    public NodeExecutionResult CreateSingleResult(JsonNode? data, bool success = true)
    {
        return new NodeExecutionResult
        {
            Success = success,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = data,
                        Success = success,
                        SourceIndex = 0
                    }
                ]
            }
        };
    }
}
