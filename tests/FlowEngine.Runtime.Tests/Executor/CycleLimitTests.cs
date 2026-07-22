using System.Collections.Generic;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// 环路失控保护（修复 #4）：反馈边激活累计超过 <see cref="EngineDefaultsOptions.MaxCycleIterations"/>
/// 时，执行应转 Failed（错误码 <c>CycleLimitExceeded</c>），而非无限运行。
/// </summary>
public sealed class CycleLimitTests
{
    [Fact]
    public async Task RunAsync_FeedbackLoopExceedsLimit_FailsWithCycleLimitExceeded()
    {
        // 极低上限，使自环节点快速触发保护。
        var kernel = BuildKernel(maxCycleIterations: 3);

        // 自环节点：始终从 Loop 端口回连自身，永不走 Done → 若无上限会死循环。
        var forever = new NodeDefinition
        {
            Id = "forever",
            Name = "forever",
            TypeName = "forever",
            IsEntry = true,
            Parameters = [],
            Ports = new List<PortInstance>()
        };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "cycle",
            CreatedBy = "test",
            Nodes = [forever],
            Connections =
            [
                // forever.Loop → forever.Input（自环回边）
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = forever.Id,
                    SourcePortName = FlowConstants.PortNames.Loop,
                    TargetNodeId = forever.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };

        var trigger = new DataBatch
        {
            Items = { new DataItem { Data = new JsonObject { ["x"] = 1 }, Success = true, SourceIndex = 0 } }
        };

        var (record, _, _) = await RunAsync(kernel, workflow, trigger);

        Assert.Equal(ExecutionStatus.Failed, record.Status);
        Assert.Contains(
            record.NodeRecords,
            r => r.Output is { Success: false } && r.Output.Error?.Code == "CycleLimitExceeded");
    }

    [Fact]
    public async Task RunAsync_NormalLoopWithinLimit_Completes()
    {
        // 正常 Loop 回环（batchSize=2，5 项）反馈激活仅 3 次，远低于上限，应正常完成。
        var kernel = BuildKernel(maxCycleIterations: 10000);

        var loopNode = new NodeDefinition
        {
            Id = "loop",
            Name = "loop",
            TypeName = "loop",
            IsEntry = true,
            Parameters = new Dictionary<string, object> { ["batchSize"] = 2 },
            Ports = new List<PortInstance>()
        };
        var processNode = new NodeDefinition
        {
            Id = "process",
            Name = "process",
            TypeName = "loopProcess",
            Parameters = [],
            Ports = new List<PortInstance>()
        };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "loopOk",
            CreatedBy = "test",
            Nodes = [loopNode, processNode],
            Connections =
            [
                new Connection { Id = Guid.NewGuid(), SourceNodeId = loopNode.Id, SourcePortName = FlowConstants.PortNames.Loop, TargetNodeId = processNode.Id, TargetPortName = FlowConstants.PortNames.Input },
                new Connection { Id = Guid.NewGuid(), SourceNodeId = processNode.Id, SourcePortName = FlowConstants.PortNames.Output, TargetNodeId = loopNode.Id, TargetPortName = FlowConstants.PortNames.Input }
            ]
        };

        var trigger = new DataBatch();
        for (var i = 0; i < 5; i++)
        {
            trigger.Items.Add(new DataItem { Data = new JsonObject { ["index"] = i }, Success = true, SourceIndex = i });
        }

        var (record, _, _) = await RunAsync(kernel, workflow, trigger);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
    }

    private static WorkflowSchedulerKernel BuildKernel(int maxCycleIterations)
    {
        var registry = new NodeRegistry(
            new INodeType[] { new LoopNode(), new LoopProcessNode(), new ForeverNode() },
            NullLogger<NodeRegistry>.Instance);
        var resolver = new ParameterResolver(
            NullLogger<ParameterResolver>.Instance,
            Options.Create(new JsEngineOptions()),
            new ScriptCache(Options.Create(new JsEngineOptions())));
        var contextFactory = new NodeExecutionContextFactory(
            registry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            resolver,
            new StubCredentialAccessor(),
            new HashSet<string>());
        return new WorkflowSchedulerKernel(
            registry,
            contextFactory,
            new ErrorStrategyHandler(),
            new SecretMasker(),
            NullLogger<WorkflowSchedulerKernel>.Instance,
            Options.Create(new EngineDefaultsOptions { MaxCycleIterations = maxCycleIterations }));
    }

    private static async Task<(ExecutionRecord Record, ExecutionSession Session, CollectingSideEffects SideEffects)> RunAsync(
        WorkflowSchedulerKernel kernel,
        Workflow workflow,
        object? triggerPayload = null)
    {
        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };

        var session = new ExecutionSession(workflow, executionRecord, executionRecord.Id)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };

        var sideEffects = new CollectingSideEffects();
        await kernel.RunAsync(session, sideEffects, triggerPayload, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        return (executionRecord, session, sideEffects);
    }

    /// <summary>
    /// 始终从 Loop 端口回连自身（永不走 Done），用于触发环路失控保护。
    /// </summary>
    private sealed class ForeverNode : INodeType
    {
        public string TypeName => "forever";
        public string DisplayName => "Forever";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Loop, DisplayName = "Loop", Direction = PortDirection.Output, Type = PortType.Main }
        ];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            var input = context.GetInputBatch();
            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch { Items = input.Items.ToList() },
                BranchIndex = 0 // 永远走 Loop，回连自身
            });
        }
    }

    /// <summary>
    /// 与集成测试一致的下游处理节点：为每项追加 processed=true 后回灌。
    /// </summary>
    private sealed class LoopProcessNode : INodeType
    {
        public string TypeName => "loopProcess";
        public string DisplayName => "Loop Process";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
        ];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            var input = context.GetInputBatch();
            var outItems = new List<DataItem>();
            foreach (var item in input.Items)
            {
                var obj = item.Data is JsonObject o ? (JsonObject)o.DeepClone() : new JsonObject();
                obj["processed"] = true;
                outItems.Add(new DataItem { Data = obj, Success = true, SourceIndex = item.SourceIndex });
            }

            return Task.FromResult(new NodeExecutionResult { Success = true, Output = new DataBatch { Items = outItems } });
        }
    }

    private sealed class CollectingSideEffects : IExecutionSideEffects
    {
        public Task PersistNodeRecordAsync(NodeExecutionRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistFailedStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistExecutionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishNodeStartedAsync(Guid executionId, string nodeId, int runIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishCompletedAsync(ExecutionStatus status, CancellationToken cancellationToken, NodeError? error = null) => Task.CompletedTask;
        public Func<LlmStreamChunk, CancellationToken, Task> CreateLlmStreamCallback(Guid executionId, string nodeId, int runIndex)
            => (_, _) => Task.CompletedTask;
    }

    private sealed class StubCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue());
        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }
}
