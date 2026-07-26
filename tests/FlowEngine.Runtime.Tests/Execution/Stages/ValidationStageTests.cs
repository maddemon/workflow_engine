using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Execution.Pipeline;
using FlowEngine.Runtime.Execution.Stages;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Execution.Stages;

/// <summary>
/// <see cref="ValidationStage"/> 行为测试：节点缺少必填参数时构造校验错误并短路，
/// 由 <see cref="PersistenceStage"/> 持久化失败结果（与真实节点失败语义一致）。
/// 复用已迁移为 <see cref="NodeBase"/> 的 <see cref="IfNode"/>（其 Condition 标记 [Required]）。
/// </summary>
public sealed class ValidationStageTests
{
    private sealed class RecordingSideEffects : IExecutionSideEffects
    {
        public int PersistCalls { get; private set; }

        public List<NodeExecutionRecord> Persisted { get; } = new();

        public Task PersistNodeRecordAsync(NodeExecutionRecord record, CancellationToken ct)
        {
            PersistCalls++;
            Persisted.Add(record);
            return Task.CompletedTask;
        }

        public Task PersistFailedStateAsync(CancellationToken ct) => Task.CompletedTask;
        public Task PersistExecutionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task PublishNodeStartedAsync(Guid executionId, string nodeId, int runIndex, CancellationToken ct) => Task.CompletedTask;
        public Task PublishCompletedAsync(ExecutionStatus status, CancellationToken ct, NodeError? error = null) => Task.CompletedTask;
        public Task PublishWorkflowStartedAsync(Guid executionId, Guid workflowDefinitionId, CancellationToken ct) => Task.CompletedTask;
        public Task PublishNodeExecutedAsync(Guid executionId, string nodeDefinitionId, int runIndex, NodeExecutionResult result, CancellationToken ct) => Task.CompletedTask;
        public Task PublishNodeErrorAsync(Guid executionId, string nodeDefinitionId, int runIndex, NodeError error, CancellationToken ct) => Task.CompletedTask;
        public Func<LlmStreamChunk, CancellationToken, Task> CreateLlmStreamCallback(Guid executionId, string nodeId, int runIndex)
            => (_, _) => Task.CompletedTask;
    }

    [Fact]
    public async Task ValidationFailed_ShortCircuitsAndPersistsFailure()
    {
        // IfNode 的 Condition 标记 [Required]；此处不声明 condition 参数，触发校验失败。
        var nodeDef = new NodeDefinition { Id = "if1", TypeName = "if", Name = "if1", ErrorStrategy = ErrorStrategy.Continue };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "t",
            Nodes = [nodeDef],
            Connections = [],
        };
        var execution = new ExecutionRecord { Id = Guid.NewGuid() };

        var registry = new NodeRegistry(new List<INodeType> { new IfNode() }, NullLogger<NodeRegistry>.Instance);
        var session = new ExecutionSession(workflow, execution, execution.Id);

        var sideEffects = new RecordingSideEffects();
        var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
        {
            [FlowConstants.PortNames.Input] = new DataBatch(),
        };
        var item = new NodeWorkItem(execution.Id, "if1", inputs, false);
        var context = new NodePipelineContext(item, session, sideEffects);

        var pipeline = new NodePipeline(new IExecutionStage[]
        {
            new InitializeStage(registry, new EngineDefaultsOptions()),
            new ValidationStage(registry),
            new ResolutionStage(),
            new ExecutionStage(null!, null!, new SecretMasker(), new EngineDefaultsOptions()),
            new PostProcessStage(),
            new RoutingStage(null!),
            new PersistenceStage(new SecretMasker()),
        });

        await pipeline.RunAsync(context, CancellationToken.None);

        // 校验错误被收集，且包含 condition 参数。
        Assert.NotEmpty(context.ValidationErrors);
        Assert.Contains(context.ValidationErrors, e => e.ParameterName == "condition");

        // 失败结果经 PersistenceStage 持久化（短路路径）。
        Assert.Equal(1, sideEffects.PersistCalls);
        Assert.NotNull(sideEffects.Persisted[0].Output.Error);
        Assert.Equal("ValidationFailed", sideEffects.Persisted[0].Output.Error!.Code);
        Assert.Equal("if1", sideEffects.Persisted[0].NodeDefinitionId);

        // 短路路径下 ShouldTerminateWorkflow 取决于错误策略；IfNode 默认 Continue 时不终止。
        Assert.False(context.ShouldTerminateWorkflow);
    }

    [Fact]
    public async Task ValidParameters_NoValidationError_Proceeds()
    {
        // 提供 condition 参数（合法表达式），校验应通过。由于 ExecutionStage 依赖为 null 不会被调用，
        // 这里仅验证校验阶段不设置 Result 且不收集错误。
        var nodeDef = new NodeDefinition
        {
            Id = "if1",
            TypeName = "if",
            Name = "if1",
            Parameters = new Dictionary<string, object> { ["condition"] = "true" },
        };
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "t", Nodes = [nodeDef], Connections = [] };
        var execution = new ExecutionRecord { Id = Guid.NewGuid() };
        var registry = new NodeRegistry(new List<INodeType> { new IfNode() }, NullLogger<NodeRegistry>.Instance);
        var session = new ExecutionSession(workflow, execution, execution.Id);

        var sideEffects = new RecordingSideEffects();
        var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
        {
            [FlowConstants.PortNames.Input] = new DataBatch(),
        };
        var item = new NodeWorkItem(execution.Id, "if1", inputs, false);
        var context = new NodePipelineContext(item, session, sideEffects);

        // 仅验证校验阶段通过并继续；不含 ExecutionStage（避免 null 依赖被调用）。
        var pipeline = new NodePipeline(new IExecutionStage[]
        {
            new InitializeStage(registry, new EngineDefaultsOptions()),
            new ValidationStage(registry),
            new PersistenceStage(new SecretMasker()),
        });

        await pipeline.RunAsync(context, CancellationToken.None);

        Assert.Empty(context.ValidationErrors);
        Assert.Null(context.Result);
    }
}
