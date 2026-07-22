using System.Collections.Generic;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
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
/// 节点级持久化上下文 —— 端到端集成：<c>Loop(batchSize=2) → Process → 回连 Loop</c>，
/// 5 项输入。验证 Loop/Process 的正确调用次数、Done 端口累积全部已处理结果，
/// 以及回环激活复用节点上下文（Task 6 / Task 7 / Task 9）。
/// </summary>
public sealed class LoopIntegrationTests
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;
    private readonly WorkflowSchedulerKernel _kernel;

    public LoopIntegrationTests()
    {
        _nodeRegistry = new NodeRegistry(
            new INodeType[] { new LoopNode(), new LoopProcessNode() },
            NullLogger<NodeRegistry>.Instance);

        var resolver = new ParameterResolver(
            NullLogger<ParameterResolver>.Instance,
            Options.Create(new JsEngineOptions()),
            new ScriptCache(Options.Create(new JsEngineOptions())));
        _contextFactory = new NodeExecutionContextFactory(
            _nodeRegistry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            resolver,
            new StubCredentialAccessor(),
            new HashSet<string>());
        _kernel = new WorkflowSchedulerKernel(
            _nodeRegistry, _contextFactory, new ErrorStrategyHandler(), new SecretMasker(), NullLogger<WorkflowSchedulerKernel>.Instance);
    }

    [Fact]
    public async Task RunAsync_LoopProcessFeedback_AccumulatesAllProcessedResults()
    {
        var loopNode = CreateNode("loop", "loop", isEntry: true, parameters: new Dictionary<string, object> { ["batchSize"] = 2 });
        var processNode = CreateNode("process", "loopProcess");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "loopFeedback",
            CreatedBy = "test",
            Nodes = [loopNode, processNode],
            Connections =
            [
                // Loop 主端口 → Process
                new Connection { Id = Guid.NewGuid(), SourceNodeId = loopNode.Id, SourcePortName = FlowConstants.PortNames.Loop, TargetNodeId = processNode.Id, TargetPortName = FlowConstants.PortNames.Input },
                // Process → 回连 Loop（回边，环路继续）
                new Connection { Id = Guid.NewGuid(), SourceNodeId = processNode.Id, SourcePortName = FlowConstants.PortNames.Output, TargetNodeId = loopNode.Id, TargetPortName = FlowConstants.PortNames.Input }
            ]
        };

        // 5 项触发负载。
        var trigger = new DataBatch
        {
            Items =
            {
                new DataItem { Data = new JsonObject { ["index"] = 0 }, Success = true, SourceIndex = 0 },
                new DataItem { Data = new JsonObject { ["index"] = 1 }, Success = true, SourceIndex = 1 },
                new DataItem { Data = new JsonObject { ["index"] = 2 }, Success = true, SourceIndex = 2 },
                new DataItem { Data = new JsonObject { ["index"] = 3 }, Success = true, SourceIndex = 3 },
                new DataItem { Data = new JsonObject { ["index"] = 4 }, Success = true, SourceIndex = 4 },
            }
        };

        var (record, session, _) = await RunAsync(workflow, trigger);

        Assert.Equal(ExecutionStatus.Completed, record.Status);

        // Loop 共执行 4 次（1 次初始化 + 3 次回环），Process 执行 3 次。
        Assert.Equal(4, record.NodeRecords.Count(r => r.NodeDefinitionId == loopNode.Id));
        Assert.Equal(3, record.NodeRecords.Count(r => r.NodeDefinitionId == processNode.Id));

        // SuccessfulOutputs 无条件累积节点每次成功输出：3 次 Loop 中间窗口（共 5 项原始输入）+
        // Done 端口累积的 5 项已处理结果，合计 10 项；末段 5 项即 Done 输出。
        Assert.True(session.SuccessfulOutputs.TryGetValue("loop", out var all));
        Assert.Equal(10, all.Items.Count);
        var done = all.Items.Skip(5).ToList();
        // 每项为下游 Process 处理后回灌（带 processed 标记），证明回环激活复用了节点上下文并累积。
        Assert.All(done, i => Assert.Equal(true, i.Data?["processed"]?.GetValue<bool>()));
        Assert.Equal(0, done[0].Data?["index"]?.GetValue<int>());
        Assert.Equal(4, done[4].Data?["index"]?.GetValue<int>());
    }

    private async Task<(ExecutionRecord Record, ExecutionSession Session, CollectingSideEffects SideEffects)> RunAsync(
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

        var session = new ExecutionSession(workflow, executionRecord, executionRecord.Id, _nodeRegistry)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };

        var sideEffects = new CollectingSideEffects();
        await _kernel.RunAsync(session, sideEffects, triggerPayload, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        return (executionRecord, session, sideEffects);
    }

    private static NodeDefinition CreateNode(
        string name,
        string typeName,
        bool isEntry = false,
        Dictionary<string, object>? parameters = null)
    {
        return new NodeDefinition
        {
            Id = name,
            Name = name,
            TypeName = typeName,
            IsEntry = isEntry,
            Parameters = parameters ?? [],
        };
    }

    /// <summary>
    /// 模拟下游处理节点：为每项的 JSON 追加 processed=true 标记后原样回灌，供 Loop 累积。
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
        public Task PublishCompletedAsync(ExecutionStatus status, CancellationToken cancellationToken) => Task.CompletedTask;
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
