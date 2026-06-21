using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 构造节点执行上下文。
/// </summary>
public sealed class NodeExecutionContextFactory
{
    private readonly INodeRegistry _registry;
    private readonly ExpressionEvaluator _expressionEvaluator;
    private readonly ParameterResolver _parameterResolver;
    private readonly ICredentialAccessor _credentialAccessor;
    private readonly IReadOnlySet<string> _environmentWhitelist;
    private readonly ParameterHydrator _parameterHydrator;
    private readonly ILlmClient? _llmClient;
    private readonly FlowEngineDbContext? _dbContext;

    /// <summary>
    /// 初始化工厂。
    /// </summary>
    public NodeExecutionContextFactory(
        INodeRegistry registry,
        ExpressionEvaluator expressionEvaluator,
        ParameterResolver parameterResolver,
        ICredentialAccessor credentialAccessor,
        IReadOnlySet<string> environmentWhitelist,
        ILogger<ParameterHydrator>? hydratorLogger = null,
        ILlmClient? llmClient = null,
        FlowEngineDbContext? dbContext = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _expressionEvaluator = expressionEvaluator ?? throw new ArgumentNullException(nameof(expressionEvaluator));
        _parameterResolver = parameterResolver ?? throw new ArgumentNullException(nameof(parameterResolver));
        _credentialAccessor = credentialAccessor ?? throw new ArgumentNullException(nameof(credentialAccessor));
        _environmentWhitelist = environmentWhitelist ?? throw new ArgumentNullException(nameof(environmentWhitelist));
        _parameterHydrator = new ParameterHydrator(credentialAccessor, hydratorLogger);
        _llmClient = llmClient;
        _dbContext = dbContext;
    }

    /// <summary>
    /// 创建节点执行上下文。
    /// </summary>
    /// <param name="workflow">工作流。</param>
    /// <param name="execution">执行记录。</param>
    /// <param name="node">节点实例（工作流定义）。</param>
    /// <param name="nodeInstance">节点类型实例（由 WorkflowExecutor 创建，贯穿 Hydrate → ExecuteAsync）。</param>
    /// <param name="inputs">输入数据。</param>
    /// <param name="nodeOutputs">已完成的节点输出。</param>
    /// <param name="nodeBatches">节点批次数据。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<NodeExecutionContext> CreateAsync(
        Workflow workflow,
        ExecutionRecord execution,
        NodeDefinition node,
        INodeType nodeInstance,
        IReadOnlyDictionary<string, DataBatch> inputs,
        IReadOnlyDictionary<string, DataBatch> nodeOutputs,
        IReadOnlyDictionary<string, DataBatch> nodeBatches,
        int runIndex,
        CancellationToken cancellationToken)
    {
        var nodeDefinition = node;
        var descriptor = _registry.GetDescriptor(node.TypeName);
        var rawParameters = MergeParameters(nodeDefinition, descriptor);
        var metadata = new ExpressionMetadata
        {
            Workflow = workflow,
            ExecutionId = execution.Id,
            RunIndex = runIndex
        };

        var expressionContext = new ExpressionContext
        {
            Inputs = inputs,
            RawParameters = rawParameters,
            NodeOutputs = nodeOutputs,
            NodeBatches = nodeBatches,
            EnvironmentWhitelist = _environmentWhitelist,
            Metadata = metadata
        };

        var resolvedParameters = _parameterResolver.Resolve(rawParameters, expressionContext);

        await _parameterHydrator.HydrateAsync(nodeInstance, resolvedParameters).ConfigureAwait(false);

        return new NodeExecutionContext
        {
            Workflow = workflow,
            ExecutionId = execution.Id,
            Node = nodeDefinition,
            RunIndex = runIndex,
            Inputs = inputs,
            RawParameters = rawParameters,
            ResolvedParameters = resolvedParameters,
            Credentials = _credentialAccessor,
            Logger = NullExecutionLogger.Instance,
            CancellationToken = cancellationToken,
            LlmClient = _llmClient,
            NodeRegistry = _registry
        };
    }

    private static Dictionary<string, object> MergeParameters(
        NodeDefinition nodeDefinition,
        NodeTypeDescriptor descriptor)
    {
        var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in descriptor.Parameters)
        {
            if (nodeDefinition.Parameters.TryGetValue(parameter.Name, out var value))
            {
                merged[parameter.Name] = value;
            }
            else if (parameter.DefaultValue is not null)
            {
                merged[parameter.Name] = parameter.DefaultValue;
            }
        }

        foreach (var (key, value) in nodeDefinition.Parameters)
        {
            if (!merged.ContainsKey(key))
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    private sealed class NullExecutionLogger : IExecutionLogger
    {
        public static readonly NullExecutionLogger Instance = new();

        public void LogInformation(string message, params object?[] args)
        {
        }

        public void LogWarning(string message, params object?[] args)
        {
        }

        public void LogError(Exception? exception, string message, params object?[] args)
        {
        }
    }
}
