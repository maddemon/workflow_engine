using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Runtime.Tests.Execution;

/// <summary>
/// Step 8 并发隔离回归：验证 <see cref="ExecutionStage"/> 建立的“每运行实例隔离”契约
/// （先经 <see cref="NodeExecutionContextFactory.CreateAsync"/> 构造每运行 <see cref="NodeExecutionContext"/>，
/// 再经 <see cref="NodeCapabilityInjector.Inject"/> 注入运行期能力）。
/// 同一节点类型并行执行必须得到相互独立的实例 / 上下文 / 引擎 / 凭据 / 类型化参数。
/// </summary>
public sealed class ConcurrencyIsolationTests
{
    /// <summary>标记身份的凭据访问器：仅用于区分两次运行的凭据来源，不实际解析凭据。</summary>
    private sealed class TaggedAccessor : ICredentialAccessor
    {
        /// <summary>身份标记，用于断言两次运行的凭据访问器互不串改。</summary>
        public string Tag { get; }

        public TaggedAccessor(string tag) => Tag = tag;

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }

    /// <summary>空凭据访问器：仅用于工厂构造（运行级隔离由 credentialAccessorOverride 驱动，不实际解析）。</summary>
    private sealed class NullCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue>(null!);

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }

    /// <summary>隔离探针节点：声明运行期能力（上下文/引擎/凭据）与 DI 能力（Sub），以及类型化参数 Label。</summary>
    [NodeMeta(TypeName = "isolSpy", DisplayName = "IsolSpy", Category = NodeCategory.Test, Icon = "spy")]
    public sealed class IsolationSpyNode : NodeBase
    {
        /// <summary>经 <see cref="NodeCapabilityInjector"/> 注入的运行上下文（每运行独立）。</summary>
        [Inject] public NodeExecutionContext? Ctx { get; private set; }

        /// <summary>经 <see cref="NodeCapabilityInjector"/> 注入的每上下文 JsEngine。</summary>
        [Inject] public JsEngine? Engine { get; private set; }

        /// <summary>经 <see cref="NodeCapabilityInjector"/> 注入的凭据访问器（每运行独立）。</summary>
        [Inject] public ICredentialAccessor? Creds { get; private set; }

        /// <summary>DI 能力：测试中 serviceProvider 为 null，预期为 null（能力隔离仅聚焦运行上下文派生项）。</summary>
        [Inject] public ISubExecutionService? Sub { get; private set; }

        /// <summary>类型化参数，由 <see cref="ParameterHydrator"/> 从 NodeDefinition.Parameters["label"] 水合（每实例独立）。</summary>
        public string Label { get; set; } = "";

        /// <inheritdoc />
        public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
            => Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
    }

    private static NodeExecutionContextFactory BuildFactory(INodeRegistry registry, ICredentialAccessor creds)
    {
        return new NodeExecutionContextFactory(
            registry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            new ParameterResolver(
                NullLogger<ParameterResolver>.Instance,
                Options.Create(new JsEngineOptions()),
                new ScriptCache(Options.Create(new JsEngineOptions()))),
            creds,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ParallelSameType_IsolatedContextEngineCredentialsParameters()
    {
        var registry = new NodeRegistry(new INodeType[] { new IsolationSpyNode() }, NullLogger<NodeRegistry>.Instance);
        // 工厂级凭据无关紧要：运行级隔离由 credentialAccessorOverride 驱动。
        var factory = BuildFactory(registry, new NullCredentialAccessor());

        var tagA = new TaggedAccessor("A");
        var tagB = new TaggedAccessor("B");

        var nodeDefA = new NodeDefinition { Id = "sA", Name = "sA", TypeName = "isolSpy", Parameters = new Dictionary<string, object> { ["label"] = "A" } };
        var nodeDefB = new NodeDefinition { Id = "sB", Name = "sB", TypeName = "isolSpy", Parameters = new Dictionary<string, object> { ["label"] = "B" } };
        var workflowA = new Workflow { Id = Guid.NewGuid(), Name = "wA", Nodes = [nodeDefA] };
        var workflowB = new Workflow { Id = Guid.NewGuid(), Name = "wB", Nodes = [nodeDefB] };
        var execA = new ExecutionRecord { Id = Guid.NewGuid() };
        var execB = new ExecutionRecord { Id = Guid.NewGuid() };

        // CreateInstance 每次返回全新克隆 —— 两次运行使用不同实例。
        var nodeA = (IsolationSpyNode)registry.CreateInstance("isolSpy");
        var nodeB = (IsolationSpyNode)registry.CreateInstance("isolSpy");

        var empty = new Dictionary<string, DataBatch>();

        // 真正并行启动两次 CreateAsync（两者均在 await 前发起），暴露任何共享静态状态缺陷。
        var ctxTaskA = factory.CreateAsync(workflowA, execA, nodeDefA, nodeA, empty, empty, empty, 0, CancellationToken.None, credentialAccessorOverride: tagA);
        var ctxTaskB = factory.CreateAsync(workflowB, execB, nodeDefB, nodeB, empty, empty, empty, 0, CancellationToken.None, credentialAccessorOverride: tagB);
        var ctxA = await ctxTaskA;
        var ctxB = await ctxTaskB;

        // 注入运行期能力（上下文已在并行 CreateAsync 中隔离，引擎随注入各自创建）。
        NodeCapabilityInjector.Inject((NodeBase)nodeA, null, ctxA);
        NodeCapabilityInjector.Inject((NodeBase)nodeB, null, ctxB);

        // ===== Step 8 回归保证 =====
        // 1. 实例隔离
        Assert.NotSame(nodeA, nodeB);

        // 2. 上下文隔离（每运行独立上下文）
        Assert.NotSame(ctxA, ctxB);

        // 3. 引擎隔离（GetOrCreateEngine 为每上下文独立）
        Assert.NotNull(nodeA.Engine);
        Assert.NotNull(nodeB.Engine);
        Assert.NotSame(nodeA.Engine, nodeB.Engine);

        // 4. 上下文派生能力各自指向自己的上下文
        Assert.Same(nodeA.Ctx, ctxA);
        Assert.Same(nodeB.Ctx, ctxB);

        // 5. 凭据访问器隔离（无交叉）
        Assert.Same(nodeA.Creds, tagA);
        Assert.Same(nodeB.Creds, tagB);

        // 6. 类型化参数绑定为每实例，不共享
        Assert.Equal("A", ((IsolationSpyNode)nodeA).Label);
        Assert.Equal("B", ((IsolationSpyNode)nodeB).Label);
    }

    [Fact]
    public async Task ParallelSameType_EnginesIndependentlyDisposable()
    {
        var registry = new NodeRegistry(new INodeType[] { new IsolationSpyNode() }, NullLogger<NodeRegistry>.Instance);
        var factory = BuildFactory(registry, new NullCredentialAccessor());

        var tagA = new TaggedAccessor("A");
        var tagB = new TaggedAccessor("B");

        var nodeDefA = new NodeDefinition { Id = "sA", Name = "sA", TypeName = "isolSpy", Parameters = new Dictionary<string, object> { ["label"] = "A" } };
        var nodeDefB = new NodeDefinition { Id = "sB", Name = "sB", TypeName = "isolSpy", Parameters = new Dictionary<string, object> { ["label"] = "B" } };
        var workflowA = new Workflow { Id = Guid.NewGuid(), Name = "wA", Nodes = [nodeDefA] };
        var workflowB = new Workflow { Id = Guid.NewGuid(), Name = "wB", Nodes = [nodeDefB] };
        var execA = new ExecutionRecord { Id = Guid.NewGuid() };
        var execB = new ExecutionRecord { Id = Guid.NewGuid() };

        var nodeA = (IsolationSpyNode)registry.CreateInstance("isolSpy");
        var nodeB = (IsolationSpyNode)registry.CreateInstance("isolSpy");
        var empty = new Dictionary<string, DataBatch>();

        var ctxTaskA = factory.CreateAsync(workflowA, execA, nodeDefA, nodeA, empty, empty, empty, 0, CancellationToken.None, credentialAccessorOverride: tagA);
        var ctxTaskB = factory.CreateAsync(workflowB, execB, nodeDefB, nodeB, empty, empty, empty, 0, CancellationToken.None, credentialAccessorOverride: tagB);
        var ctxA = await ctxTaskA;
        var ctxB = await ctxTaskB;

        NodeCapabilityInjector.Inject((NodeBase)nodeA, null, ctxA);
        NodeCapabilityInjector.Inject((NodeBase)nodeB, null, ctxB);

        // 捕获注入期的引擎引用
        var originalA = nodeA.Engine;
        var originalB = nodeB.Engine;
        Assert.NotNull(originalA);
        Assert.NotNull(originalB);

        // 释放 A 上下文引擎：A 可重新创建独立引擎，且不影响 B 的引擎。
        ctxA.ReleaseEngine();
        var recreatedA = ctxA.GetOrCreateEngine();

        Assert.NotSame(recreatedA, originalB);   // A 重新创建的引擎 ≠ B 的引擎
        Assert.NotSame(recreatedA, originalA);    // A 重新创建的引擎 ≠ A 原引擎（生命周期已重置）
        Assert.Same(nodeB.Engine, originalB);     // B 的引擎未被 A 的释放影响
    }
}
