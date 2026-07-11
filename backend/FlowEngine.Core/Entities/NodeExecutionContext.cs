using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Logging;

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
    /// 当前节点执行记录 ID。
    /// </summary>
    public Guid NodeExecutionRecordId { get; set; }

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
    /// 由 <c>LlmNode</c> 在自身执行时创建并写入，供下游节点复用。
    /// </summary>
    public ILlmClient? LlmClient { get; set; }

    /// <summary>
    /// LLM 客户端工厂，供 <c>LlmNode</c> 等节点按运行时参数（模型、温度、端点、凭据）创建
    /// <see cref="ILlmClient"/>。抽象定义于 Core，由宿主注入 Infrastructure 的具体实现，
    /// 使插件无需直接依赖 Infrastructure。
    /// </summary>
    public ILlmClientFactory? LlmClientFactory { get; set; }

    /// <summary>
    /// HTTP 客户端连接池，供 HTTP 请求节点使用。
    /// </summary>
    public IHttpClientPool? HttpClientPool { get; set; }

    /// <summary>
    /// 节点注册中心，供 Agent 等节点查找下游节点类型。
    /// </summary>
    public INodeRegistry? NodeRegistry { get; set; }

    /// <summary>
    /// 节点执行上下文工厂，供 Agent 等节点执行子节点。
    /// </summary>
    public INodeExecutionContextFactory? ContextFactory { get; set; }

    /// <summary>
    /// 脚本编译缓存，供 FilterNode 等需要逐项求值的节点复用已编译脚本。
    /// </summary>
    public IScriptCache? ScriptCache { get; set; }

    /// <summary>
    /// 工作流加载器，供子工作流工具节点从数据库加载工作流。
    /// </summary>
    public IWorkflowLoader? WorkflowLoader { get; set; }

    /// <summary>
    /// 当前 Agent 嵌套深度，用于防止无限递归。
    /// </summary>
    public int NestingDepth { get; set; }

    /// <summary>
    /// 工作流执行内的共享数据字典，供 MemoryNode 等节点读写跨节点数据。
    /// 由 WorkflowExecutor 在构造上下文时注入，同一执行内所有节点共享同一实例。
    /// </summary>
    public IDictionary<string, JsonNode?> Memory { get; set; } = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 由工厂注入的全局变量字典（非逐项变量），供节点在逐项求值时复用。
    /// 包含 $credentials/$env/$workflow/$execution/$vars/$now/$today/$node/$ctx 等，
    /// 不含逐项变量 $json/$input/$itemIndex/$runIndex。
    /// </summary>
    public IReadOnlyDictionary<string, object?>? GlobalVariables { get; set; }

    /// <summary>
    /// LLM 流式 token 回调，由 WorkflowExecutor 注入用于将 LLM 增量 chunk 推送到前端。
    /// 仅在 AgentNode 等使用 LLM 的节点执行时被触发。
    /// </summary>
    public Func<LlmStreamChunk, CancellationToken, Task>? OnLlmStreamChunk { get; set; }

    /// <summary>
    /// JS 引擎安全限制配置，供门面托管引擎创建时使用（由工厂注入）。为 null 时使用默认限制。
    /// </summary>
    public JsEngineOptions? EngineOptions { get; set; }

    /// <summary>
    /// JS 引擎日志器，供门面托管引擎使用（由工厂注入）。
    /// </summary>
    public ILogger<JsEngine>? EngineLogger { get; set; }

    private JsEngine? _managedEngine;

    /// <summary>
    /// 获取或创建单次节点执行托管的单个 JsEngine（懒创建、复用）。
    /// 引擎由运行时（WorkflowSchedulerKernel）在节点执行（含重试）结束后调用 <see cref="ReleaseEngine"/> 释放。
    /// </summary>
    public JsEngine GetOrCreateEngine()
    {
        if (_managedEngine is null)
        {
            _managedEngine = JsEngine.Create(EngineOptions, logger: EngineLogger);
        }

        return _managedEngine;
    }

    /// <summary>
    /// 释放托管的 JS 引擎（若存在）。节点执行结束后由运行时统一调用。
    /// </summary>
    public void ReleaseEngine()
    {
        _managedEngine?.Dispose();
        _managedEngine = null;
    }

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
            if (!Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) || batch.Items.Count == 0)
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
    /// 解析凭据并返回 API Key 或 OAuth2 accessToken。
    /// 支持按凭据 ID（Guid）或凭据名称解析；oauth2 类型返回 <c>accessToken</c>。
    /// </summary>
    public async Task<string?> ResolveApiKeyAsync(string? credentialId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(credentialId))
        {
            return null;
        }

        try
        {
            CredentialValue credential;
            if (Guid.TryParse(credentialId, out var id))
            {
                credential = await Credentials.GetCredentialAsync(id, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var byName = await Credentials.GetCredentialByNameAsync(credentialId, cancellationToken)
                    .ConfigureAwait(false);
                if (byName is null)
                {
                    return null;
                }

                credential = byName;
            }

            if (string.Equals(credential.Type, "oauth2", StringComparison.OrdinalIgnoreCase))
            {
                if (credential.Fields.TryGetValue("accessToken", out var accessToken))
                {
                    return accessToken;
                }
            }

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
