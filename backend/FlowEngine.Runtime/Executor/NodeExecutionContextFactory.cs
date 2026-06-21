using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Scripting;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 构造节点执行上下文。
/// </summary>
public sealed class NodeExecutionContextFactory
{
    private readonly INodeRegistry _registry;
    private readonly ParameterResolver _parameterResolver;
    private readonly ICredentialAccessor _credentialAccessor;
    private readonly IReadOnlySet<string> _environmentWhitelist;
    private readonly ParameterHydrator _parameterHydrator;
    private readonly ILogger<JsEngine>? _jsLogger;
    private readonly ILlmClient? _llmClient;
    private readonly FlowEngineDbContext? _dbContext;

    public NodeExecutionContextFactory(
        INodeRegistry registry,
        ParameterResolver parameterResolver,
        ICredentialAccessor credentialAccessor,
        IReadOnlySet<string> environmentWhitelist,
        ILogger<ParameterHydrator>? hydratorLogger = null,
        ILogger<JsEngine>? jsLogger = null,
        ILlmClient? llmClient = null,
        FlowEngineDbContext? dbContext = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _parameterResolver = parameterResolver ?? throw new ArgumentNullException(nameof(parameterResolver));
        _credentialAccessor = credentialAccessor ?? throw new ArgumentNullException(nameof(credentialAccessor));
        _environmentWhitelist = environmentWhitelist ?? throw new ArgumentNullException(nameof(environmentWhitelist));
        _parameterHydrator = new ParameterHydrator(credentialAccessor, hydratorLogger);
        _jsLogger = jsLogger;
        _llmClient = llmClient;
        _dbContext = dbContext;
    }

    public async Task<NodeExecutionContext> CreateAsync(
        Workflow workflow,
        ExecutionRecord execution,
        NodeDefinition node,
        INodeType nodeInstance,
        IReadOnlyDictionary<string, DataBatch> inputs,
        IReadOnlyDictionary<string, DataBatch> successfulOutputs,
        IReadOnlyDictionary<string, DataBatch> latestBatches,
        int runIndex,
        CancellationToken cancellationToken)
    {
        var nodeDefinition = node;
        var descriptor = _registry.GetDescriptor(node.TypeName);
        var rawParameters = MergeParameters(nodeDefinition, descriptor);

        using var js = JsEngine.Create(logger: _jsLogger);
        js.SetValue("input", GetCurrentInput(inputs, runIndex));
        js.SetValue("inputs", inputs);
        js.SetValue("parameter", rawParameters);
        js.SetValue("nodes", successfulOutputs);
        js.SetValue("items", latestBatches);
        js.SetValue("workflow", new Dictionary<string, object?>
        {
            ["id"] = workflow.Id,
            ["name"] = workflow.Name,
            ["projectId"] = workflow.ProjectId,
            ["version"] = workflow.Version,
            ["isActive"] = workflow.IsActive,
        });
        js.SetValue("execution", new Dictionary<string, object?>
        {
            ["id"] = execution.Id,
        });
        js.SetValue("runIndex", runIndex);
        js.SetValue("run_index", runIndex);
        js.SetValue("env", new EnvironmentAccessor(_environmentWhitelist));
        js.SetValue("now", DateTime.UtcNow);

        var resolvedParameters = _parameterResolver.Resolve(rawParameters, js);

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

    private static object? GetCurrentInput(IReadOnlyDictionary<string, DataBatch> inputs, int runIndex)
    {
        if (!inputs.TryGetValue("input", out var batch) || batch.Items.Count == 0)
        {
            return null;
        }

        var index = runIndex >= 0 && runIndex < batch.Items.Count ? runIndex : 0;
        return batch.Items[index].Data;
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
        public void LogInformation(string message, params object?[] args) { }
        public void LogWarning(string message, params object?[] args) { }
        public void LogError(Exception? exception, string message, params object?[] args) { }
    }
}
