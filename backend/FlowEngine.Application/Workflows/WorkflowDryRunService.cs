using FlowEngine.Application.Authorization;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Dtos;
using FlowEngine.Core;
using Mapster;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Security;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流 Dry-Run 服务，直接在内存中构建 DSL 定义并复用 <see cref="WorkflowSchedulerKernel"/> 执行，不持久化任何记录。
/// </summary>
public sealed class WorkflowDryRunService(
    INodeRegistry nodeRegistry,
    NodeExecutionContextFactory contextFactory,
    ILogger<WorkflowSchedulerKernel> kernelLogger,
    SecretMasker secretMasker,
    IAuthorizationGuard authGuard,
    ICredentialAccessor realCredentialAccessor)
{
    /// <summary>
    /// 对传入的 DSL 工作流执行 Dry-Run。
    /// </summary>
    /// <param name="request">Dry-Run 请求，包含节点、连接、输入与临时凭据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果 DTO。</returns>
    public async Task<ExecutionDto> DryRunAsync(
        DryRunWorkflowRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await authGuard.RequireScopeAsync(Scope.Workflow, Operation.Execute, cancellationToken);

        var workflow = BuildWorkflow(request);
        var credentialAccessor = BuildCredentialAccessor(request.Credentials, realCredentialAccessor);
        var sensitiveValues = ExtractSensitiveValues(request.Credentials);
        await PreResolveCredentialParameters(workflow, credentialAccessor, cancellationToken).ConfigureAwait(false);

        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            ProjectId = workflow.ProjectId,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Running,
            NodeRecords = []
        };

        var session = new ExecutionSession(workflow, executionRecord, executionRecord.Id, nodeRegistry)
        {
            CredentialAccessor = credentialAccessor,
            SensitiveValues = sensitiveValues
        };

        var kernel = new WorkflowSchedulerKernel(
            nodeRegistry,
            contextFactory,
            new ErrorStrategyHandler(),
            secretMasker,
            kernelLogger);
        var sideEffects = new DryRunSideEffects();

        await kernel.RunAsync(session, sideEffects, request.Inputs, cancellationToken).ConfigureAwait(false);

        // Dry-Run 终态沿用独立的 DryRunCompleted 语义（区别于真实执行的 Completed）。
        if (session.Execution.Status == ExecutionStatus.Completed)
        {
            executionRecord.Status = ExecutionStatus.DryRunCompleted;
        }

        return ExecutionMapper.MapToDto(executionRecord);
    }

    private static Workflow BuildWorkflow(DryRunWorkflowRequestDto request)
    {
        var nodes = request.Nodes.Select(n => n.Adapt<NodeDefinition>()).ToList();
        var connections = request.Connections.Select(c => c.Adapt<Connection>()).ToList();

        // 不再强制 Continue：尊重请求中各节点的错误策略（未指定时默认为 Terminate），
        // 以便单节点失败时按配置正确终止执行。
        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "dry-run",
            CreatedBy = "dry-run",
            IsActive = true,
            Version = 1,
            Nodes = nodes,
            Connections = connections
        };
    }

    private static ICredentialAccessor BuildCredentialAccessor(IReadOnlyCollection<DryRunCredentialDto>? credentials, ICredentialAccessor realAccessor)
    {
        var values = new Dictionary<string, CredentialValue>(StringComparer.OrdinalIgnoreCase);
        if (credentials is not null)
        {
            foreach (var credential in credentials)
            {
                values[credential.Name] = new CredentialValue
                {
                    Name = credential.Name,
                    Type = credential.Type,
                    Fields = credential.Fields,
                    BinaryFields = []
                };
            }
        }

        return new FallbackCredentialAccessor(new TemporaryCredentialAccessor(values), realAccessor);
    }

    private async Task PreResolveCredentialParameters(Workflow workflow, ICredentialAccessor credentialAccessor, CancellationToken cancellationToken)
    {
        foreach (var node in workflow.Nodes)
        {
            var descriptor = nodeRegistry.GetDescriptor(node.TypeName);
            var credentialParameters = descriptor.Parameters
                .Where(p => p.Type == ParameterType.Credential)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var key in node.Parameters.Keys.ToList())
            {
                if (!credentialParameters.Contains(key))
                {
                    continue;
                }

                var value = node.Parameters[key];
                if (value is string credentialName)
                {
                    try
                    {
                        var credential = await credentialAccessor.GetCredentialByNameAsync(credentialName, cancellationToken).ConfigureAwait(false);
                        if (credential is not null)
                        {
                            node.Parameters[key] = credential;
                        }
                    }
                    catch (NotFoundException)
                    {
                        // 临时凭据未找到时保留原名称，由执行阶段自行解析
                    }
                }
            }
        }
    }

    private static HashSet<string> ExtractSensitiveValues(IReadOnlyCollection<DryRunCredentialDto>? credentials)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (credentials is null)
        {
            return values;
        }

        foreach (var credential in credentials)
        {
            foreach (var fieldValue in credential.Fields.Values)
            {
                values.Add(fieldValue);
            }
        }

        return values;
    }



    /// <summary>
    /// Dry-Run 副作用实现：不落库、不发布事件（纯内存模拟）。
    /// </summary>
    private sealed class DryRunSideEffects : IExecutionSideEffects
    {
        public Task PersistNodeRecordAsync(NodeExecutionRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistFailedStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistExecutionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishNodeStartedAsync(Guid executionId, string nodeId, int runIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishCompletedAsync(ExecutionStatus status, CancellationToken cancellationToken, NodeError? error = null) => Task.CompletedTask;
        public Func<LlmStreamChunk, CancellationToken, Task> CreateLlmStreamCallback(Guid executionId, string nodeId, int runIndex)
            => (_, _) => Task.CompletedTask;
    }

    private sealed class TemporaryCredentialAccessor : ICredentialAccessor
    {
        private readonly IReadOnlyDictionary<string, CredentialValue> _credentials;

        public TemporaryCredentialAccessor(IReadOnlyDictionary<string, CredentialValue> credentials)
        {
            _credentials = credentials;
        }

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException($"Dry-run 仅支持按名称引用临时凭据，不支持 GUID '{credentialId}'。");
        }

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            _credentials.TryGetValue(name, out var value);
            return Task.FromResult(value);
        }
    }

    /// <summary>
    /// 凭据访问器：先查临时凭据，找不到时回退到真实凭据库。
    /// </summary>
    private sealed class FallbackCredentialAccessor(ICredentialAccessor primary, ICredentialAccessor fallback) : ICredentialAccessor
    {
        public async Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
        {
            try
            {
                return await primary.GetCredentialAsync(credentialId, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return await fallback.GetCredentialAsync(credentialId, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            var result = await primary.GetCredentialByNameAsync(name, cancellationToken).ConfigureAwait(false);
            if (result is not null) return result;
            return await fallback.GetCredentialByNameAsync(name, cancellationToken).ConfigureAwait(false);
        }
    }
}
