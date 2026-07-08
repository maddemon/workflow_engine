using System;
using System.Security.Cryptography;
using System.Text.Json;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Expressions;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Scripting;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 构造节点执行上下文。
/// </summary>
public sealed class NodeExecutionContextFactory(
    INodeRegistry registry,
    ParameterResolver parameterResolver,
    ICredentialAccessor credentialAccessor,
    IReadOnlySet<string> environmentWhitelist,
    ILogger<ParameterHydrator>? hydratorLogger = null,
    ILogger<JsEngine>? jsLogger = null,
    JsEngineOptions? jsEngineOptions = null,
    ILlmClient? llmClient = null,
    IWorkflowLoader? workflowLoader = null,
    IHttpClientPool? httpClientPool = null) : Core.Abstractions.INodeExecutionContextFactory
{
    private readonly ParameterHydrator ParameterHydrator = new(credentialAccessor, hydratorLogger);

    public async Task<NodeExecutionContext> CreateAsync(
        Workflow workflow,
        ExecutionRecord execution,
        NodeDefinition node,
        INodeType nodeInstance,
        IReadOnlyDictionary<string, DataBatch> inputs,
        IReadOnlyDictionary<string, DataBatch> successfulOutputs,
        IReadOnlyDictionary<string, DataBatch> latestBatches,
        int runIndex,
        CancellationToken cancellationToken,
        ICredentialAccessor? credentialAccessorOverride = null)
    {
        var nodeDefinition = node;
        var descriptor = registry.GetDescriptor(node.TypeName);
        var rawParameters = MergeParameters(nodeDefinition, descriptor);
        var cacheKey = BuildCacheKey(descriptor);

        // CodeEditor/Script 参数由节点自己执行，跳过表达式求值
        var codeParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in descriptor.Parameters)
        {
            if (p.Hint is PresentationHint.CodeEditor or PresentationHint.Script)
            {
                codeParamNames.Add(p.Name);
            }
        }

        var rawCodeParams = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (codeParamNames.Count > 0)
        {
            foreach (var name in codeParamNames)
            {
                if (rawParameters.Remove(name, out var val))
                {
                    rawCodeParams[name] = val;
                }
            }
        }

        using var js = JsEngine.Create(options: jsEngineOptions, logger: jsLogger);
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
        js.SetValue("env", new EnvironmentAccessor(environmentWhitelist));
        js.SetValue("now", DateTime.UtcNow);

        var resolvedParameters = parameterResolver.Resolve(rawParameters, js, cacheKey);

        // 将 CodeEditor/Script 参数原样放回
        foreach (var (name, val) in rawCodeParams)
        {
            resolvedParameters[name] = val;
        }

        var hydrator = credentialAccessorOverride is null
            ? ParameterHydrator
            : new ParameterHydrator(credentialAccessorOverride, hydratorLogger);
        await hydrator.HydrateAsync(nodeInstance, resolvedParameters).ConfigureAwait(false);

        return new NodeExecutionContext
        {
            Workflow = workflow,
            ExecutionId = execution.Id,
            Node = nodeDefinition,
            RunIndex = runIndex,
            Inputs = inputs,
            RawParameters = rawParameters,
            ResolvedParameters = resolvedParameters,
            Credentials = credentialAccessorOverride ?? credentialAccessor,
            Logger = NullExecutionLogger.Instance,
            CancellationToken = cancellationToken,
            LlmClient = llmClient,
            HttpClientPool = httpClientPool,
            NodeRegistry = registry,
            ContextFactory = this,
            WorkflowLoader = workflowLoader,
        };
    }

    private static ExpressionCacheKey? BuildCacheKey(NodeTypeDescriptor descriptor)
    {
        var inputPort = descriptor.Ports.FirstOrDefault(p =>
            p.Name.Equals(FlowConstants.PortNames.Input, StringComparison.OrdinalIgnoreCase)
            && p.Direction == PortDirection.Input
            && p.Type == PortType.Main);

        var inputSchemaHash = ComputeHash(inputPort?.ExpectedSchema);
        var parameterSchemaHash = ComputeHash(descriptor.Parameters);

        if (string.IsNullOrEmpty(inputSchemaHash) && string.IsNullOrEmpty(parameterSchemaHash))
        {
            return null;
        }

        return new ExpressionCacheKey(string.Empty, inputSchemaHash, parameterSchemaHash);
    }

    private static string ComputeHash(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var json = JsonSerializer.Serialize(value, JsonDefaults.Options);
        if (string.IsNullOrEmpty(json) || json == "{}")
        {
            return string.Empty;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
#if NET8_0_OR_GREATER
        var hash = SHA256.HashData(bytes);
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
#endif
        return Convert.ToHexString(hash);
    }

    private static object? GetCurrentInput(IReadOnlyDictionary<string, DataBatch> inputs, int runIndex)
    {
        if (!inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) || batch.Items.Count == 0)
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
