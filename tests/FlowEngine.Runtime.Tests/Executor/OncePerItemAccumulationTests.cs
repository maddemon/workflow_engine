using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// OncePerItem è¾“å‡ºç´¯ç§¯éªŒè¯ï¼ˆä¿®å¤? #5ï¼šé€é¡¹è¿è¡Œè¦†ç›–å¼èµ‹å€¼åªä¿ç•™æœ€åŽä¸€é¡¹ï¼‰ã€?
/// ä¿®å¤åŽæ¯æ¬¡è¿è¡Œçš„è¾“å‡ºåº”è¿½åŠ åˆ°ç´¯ç§¯æ‰¹ï¼Œä¸‹æ¸¸èŠ‚ç‚¹æ®æ­¤æ‹¿åˆ°å…¨éƒ¨é¡¹è¾“å‡ºï¼Œè€Œéžä»…æœ€åŽä¸€é¡¹ã€?
/// </summary>
public sealed class OncePerItemAccumulationTests
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;
    private readonly WorkflowSchedulerKernel _kernel;

    public OncePerItemAccumulationTests()
    {
        _nodeRegistry = new NodeRegistry(
            [new PassThroughNode(), new OncePerItemNode()],
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

    // è¾¹ç•Œï¼šå•è¾“å…¥é¡¹æ—¶ OncePerItem èŠ‚ç‚¹è¾“å‡ºç´¯ç§¯ä¸? 1 é¡¹ï¼ˆä¸ä¸¢é¡¹ã€ä¸é‡å¤ï¼‰ã€?
    [Fact]
    public async Task RunAsync_OncePerItem_SingleItem_ProducesOneOutput()
    {
        var (record, session, _) = await RunOncePerItemAsync([42]);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.True(session.SuccessfulOutputs.TryGetValue("a", out var outputs));
        Assert.Single(outputs.Items);
        Assert.Equal(0, outputs.Items[0].SourceIndex);
    }

    // æ­£å¸¸è·¯å¾„ï¼šå¤šè¾“å…¥é¡¹æ—¶ OncePerItem èŠ‚ç‚¹æ‰€æœ‰è¿è¡Œè¾“å‡ºè¢«ç´¯ç§¯åˆ? session.SuccessfulOutputsï¼?
    // ä¸‹æ¸¸ç»? $node.<name> è¯»å–æ—¶èƒ½æ‹¿åˆ°å…¨éƒ¨é¡¹ï¼ˆè€Œéžä»…æœ€åŽä¸€é¡¹ï¼‰ã€?
    [Fact]
    public async Task RunAsync_OncePerItem_AccumulatesAllItemOutputsPreservingContent()
    {
        var (record, session, _) = await RunOncePerItemAsync([10, 20, 30]);

        Assert.Equal(ExecutionStatus.Completed, record.Status);

        // æºèŠ‚ç‚? a çš? 3 ä¸ªè¾“å…¥é¡¹å„è‡ªè¿è¡Œä¸€æ¬¡ï¼Œè¾“å‡ºåº”è¢«ç´¯ç§¯ä¸? 3 é¡¹ï¼ˆè€Œéžè¢«è¦†ç›–ä¸º 1 é¡¹ï¼‰ã€?
        Assert.True(session.SuccessfulOutputs.TryGetValue("a", out var outputs));
        Assert.Equal(3, outputs.Items.Count);

        // ç´¯ç§¯å†…å®¹å®Œæ•´ï¼šæ¯æ¬¡è¿è¡Œçš„è¾“å‡ºï¼ˆData == RunIndexï¼‰æŒ‰ SourceIndex 0/1/2 ä¿ç•™ï¼Œæ— è¦†ç›–ä¸¢å¤±ã€?
        Assert.Contains(outputs.Items, i => i.SourceIndex == 0 && i.Data?.GetValue<int>() == 0);
        Assert.Contains(outputs.Items, i => i.SourceIndex == 1 && i.Data?.GetValue<int>() == 1);
        Assert.Contains(outputs.Items, i => i.SourceIndex == 2 && i.Data?.GetValue<int>() == 2);
    }

    // FIX 1 å›žå½’ï¼šOncePerItem æºèŠ‚ç‚¹ç» EDGE è·¯ç”±åˆ°ä¸‹æ¸¸æ”¶é›†èŠ‚ç‚¹æ—¶ï¼?
    // ä¸‹æ¸¸åº”æ”¶åˆ°ç´¯ç§¯æ‰¹ï¼ˆå…¨éƒ¨é¡¹ï¼‰ï¼Œè€Œéžä»…æœ€åŽä¸€æ¬¡è¿è¡Œçš„å•æ‰¹ï¼ˆé™é»˜ä¸¢æ•°æ®ï¼‰ã€?
    [Fact]
    public async Task RunAsync_OncePerItem_RoutesCumulativeBatchToEdgeDownstream()
    {
        var (record, session, _) = await RunOncePerItemWithEdgeAsync([10, 20, 30]);

        Assert.Equal(ExecutionStatus.Completed, record.Status);

        // ä¸‹æ¸¸èŠ‚ç‚¹ b ç»? EDGE æ”¶åˆ°çš„è¾“å…¥å³å…? PassThrough è¾“å‡ºï¼šä¿®å¤åŽåº”ä¸ºç´¯ç§¯æ‰¹ï¼ˆ3 é¡¹ï¼‰ï¼?
        // è‹¥ä¸º bugï¼ˆä»…æœ€åŽä¸€æ¬¡è¿è¡Œçš„å•æ‰¹ï¼‰åˆ™åªæœ‰ 1 é¡¹ã€?
        Assert.True(session.SuccessfulOutputs.TryGetValue("b", out var downstreamOutputs));
        Assert.Equal(3, downstreamOutputs.Items.Count);
        Assert.Contains(downstreamOutputs.Items, i => i.SourceIndex == 0 && i.Data?.GetValue<int>() == 0);
        Assert.Contains(downstreamOutputs.Items, i => i.SourceIndex == 1 && i.Data?.GetValue<int>() == 1);
        Assert.Contains(downstreamOutputs.Items, i => i.SourceIndex == 2 && i.Data?.GetValue<int>() == 2);
    }

    private async Task<(ExecutionRecord Record, ExecutionSession Session, CollectingSideEffects SideEffects)> RunOncePerItemWithEdgeAsync(int[] items, WorkflowSchedulerKernel? kernel = null)
    {
        var nodeA = CreateNode("a", "oncePerItem", isEntry: true);
        var nodeB = CreateNode("b", "passThrough", isEntry: false);
        var connection = new Connection
        {
            SourceNodeId = "a",
            TargetNodeId = "b",
            SourcePortName = "output",
            TargetPortName = "input",
        };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "oncePerItem-edge-routing",
            CreatedBy = "test",
            Nodes = [nodeA, nodeB],
            Connections = [connection],
        };

        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = [],
        };

        var session = new ExecutionSession(workflow, executionRecord, executionRecord.Id, _nodeRegistry)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };

        var sideEffects = new CollectingSideEffects();
        await (kernel ?? _kernel).RunAsync(session, sideEffects, items, TestContext.Current.CancellationToken);

        return (executionRecord, session, sideEffects);
    }

    private async Task<(ExecutionRecord Record, ExecutionSession Session, CollectingSideEffects SideEffects)> RunOncePerItemAsync(int[] items, WorkflowSchedulerKernel? kernel = null)
    {
        var nodeA = CreateNode("a", "oncePerItem", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "oncePerItem-accumulate",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = [],
        };

        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = [],
        };

        var session = new ExecutionSession(workflow, executionRecord, executionRecord.Id, _nodeRegistry)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };

        var sideEffects = new CollectingSideEffects();
        await (kernel ?? _kernel).RunAsync(session, sideEffects, items, TestContext.Current.CancellationToken);

        return (executionRecord, session, sideEffects);
    }

    // CON-5£º´óÅú´Î£¨OncePerItem£©Êä³ö¾­ MaxRetainedOutputItems ÏÞÁ÷£¬½ö±£Áô×î½ü N Ïî£¬ÄÚ´æÓÐÉÏÏÞ¡£
    [Fact]
    public async Task RunAsync_OncePerItem_CapsRetainedOutputs_WhenLimitSet()
    {
        var cappedKernel = new WorkflowSchedulerKernel(
            _nodeRegistry,
            _contextFactory,
            new ErrorStrategyHandler(),
            new SecretMasker(),
            NullLogger<WorkflowSchedulerKernel>.Instance,
            Options.Create(new EngineDefaultsOptions { MaxRetainedOutputItems = 100 }));

        var (record, session, _) = await RunOncePerItemWithEdgeAsync(Enumerable.Range(0, 500).ToArray(), cappedKernel);

        Assert.Equal(ExecutionStatus.Completed, record.Status);

        // Ô´½Úµã a µÄÊä³ö±»ÏÞÁ÷Îª×î½ü 100 Ïî£¨CON-5£©¡£
        Assert.True(session.SuccessfulOutputs.TryGetValue("a", out var source));
        Assert.Equal(100, source.Items.Count);

        // ÏÂÓÎ b ¾­Â·ÓÉÈ·ÊµÊÕµ½ÍêÕû 500 Ïî£¨Â·ÓÉÊ¹ÓÃÏÞÁ÷Ç°µÄÍêÕûÀÛ»ýÅú£¬Êý¾Ý²»¶ª£©£»
        // µ« b ×ÔÉí±£ÁôÔÚ SuccessfulOutputs µÄ¿ìÕÕÍ¬ÑùÊÜ CON-5 ÏÞÁ÷Îª×î½ü 100 Ïî£¬ÒÔÔ¼ÊøÕûÊ÷ÄÚ´æÉÏÏÞ¡£
        Assert.True(session.SuccessfulOutputs.TryGetValue("b", out var downstream));
        Assert.Equal(100, downstream.Items.Count);

        // CON-5 ¹Ø¼ü²»±äÁ¿£ºÏÞÁ÷½ö½Ø¶Ï³É¹¦Êä³ö¿ìÕÕ£¨SuccessfulOutputs£©£¬²»µÃ½Ø¶ÏÂ·ÓÉ¸øÏÂÓÎµÄÊäÈëÅú¡£
        // ½Úµã b µÄÖ´ÐÐ¼ÇÂ¼ Inputs ¼´Æä±»µ÷ÓÃÊ±Êµ¼ÊÊÕµ½µÄÂ·ÓÉÊäÈëÅú£¬±ØÐëµÈÓÚÍêÕûÔ´Åú´óÐ¡£¨500£©£¬¶ø·ÇÏÞÁ÷µÄ 100¡£
        var downstreamRecord = session.Execution.NodeRecords.First(r => r.NodeDefinitionId == "b");
        var receivedInputCount = downstreamRecord.Inputs.Values.Sum(batch => batch.Items.Count);
        Assert.Equal(500, receivedInputCount);
    }

    // CON-3£ºÁ½¸öÖ´ÐÐ²¢ÐÐÔËÐÐÍ¬Ò»ÄÚºË£¨¹²Ïí NodeRegistry£©£¬¸÷×Ô¿ËÂ¡½ÚµãÊµÀý£¬
    // ¼´±ã²¢ÐÐÖ´ÐÐÒ²²»Ó¦Ïà»¥¸ÉÈÅµ¼ÖÂ±ÀÀ£»ò×´Ì¬´®ÈÅ£¨¸ôÀëÓÉ NodeRegistry ¿ËÂ¡±£Ö¤£©¡£
    [Fact]
    public async Task RunAsync_ParallelExecutions_AreIsolated()
    {
        var taskA = RunOncePerItemAsync([1, 2, 3]);
        var taskB = RunOncePerItemAsync([4, 5, 6, 7]);
        var results = await Task.WhenAll(taskA, taskB);

        var (recordA, sessionA, _) = results[0];
        var (recordB, sessionB, _) = results[1];

        Assert.Equal(ExecutionStatus.Completed, recordA.Status);
        Assert.Equal(ExecutionStatus.Completed, recordB.Status);

        // ¸÷×ÔÀÛ»ý±¾Ö´ÐÐµÄÊäÈëÏîÊý£¬»¥²»±»¶Ô·½ÎÛÈ¾£¨a: 3 Ïî£¬b: 4 Ïî£©¡£
        Assert.True(sessionA.SuccessfulOutputs.TryGetValue("a", out var outA));
        Assert.Equal(3, outA.Items.Count);
        Assert.True(sessionB.SuccessfulOutputs.TryGetValue("a", out var outB));
        Assert.Equal(4, outB.Items.Count);
    }

    private static NodeDefinition CreateNode(
        string name,
        string typeName,
        bool isEntry = false,
        ErrorStrategy errorStrategy = ErrorStrategy.Terminate)
    {
        return new NodeDefinition
        {
            Id = name,
            Name = name,
            TypeName = typeName,
            IsEntry = isEntry,
            Parameters = [],
            ErrorStrategy = errorStrategy,
        };
    }

    private sealed class CollectingSideEffects : IExecutionSideEffects
    {
        public int PersistCalls { get; private set; }

        public Task PersistNodeRecordAsync(NodeExecutionRecord record, CancellationToken cancellationToken)
        {
            PersistCalls++;
            return Task.CompletedTask;
        }

        public Task PersistFailedStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistExecutionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishNodeStartedAsync(Guid executionId, string nodeId, int runIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishCompletedAsync(ExecutionStatus status, CancellationToken cancellationToken, NodeError? error = null) => Task.CompletedTask;
        public Task PublishWorkflowStartedAsync(Guid executionId, Guid workflowDefinitionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishNodeExecutedAsync(Guid executionId, string nodeDefinitionId, int runIndex, NodeExecutionResult result, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishNodeErrorAsync(Guid executionId, string nodeDefinitionId, int runIndex, NodeError error, CancellationToken cancellationToken) => Task.CompletedTask;
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
