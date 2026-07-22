using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Agent;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Tests;

/// <summary>
/// <see cref="ToolContextFactory.CreateAsync"/> 额外行为测试，覆盖主测试未触及的分支：
/// <list type="bullet">
///   <item><description>Activator 实例化工具节点失败（无参构造函数缺失）时，回退到解析出的节点类型实例并记录警告。</description></item>
///   <item><description>父上下文提供 <see cref="INodeExecutionContextFactory"/> 时，经工厂创建子上下文并显式递增嵌套深度。</description></item>
/// </list>
/// </summary>
public class ToolContextFactoryExtraTests
{
    private sealed class FakeNodeType : INodeType
    {
        public string TypeName => "fake";
        public string DisplayName => "Fake";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });

        public NodeTypeDescriptor GetDescriptor() => new() { TypeName = "fake" };
    }

    // 仅含带参构造函数，使 Activator.CreateInstance 失败，触发回退分支。
    private sealed class FakeNodeTypeNoCtor : INodeType
    {
        public FakeNodeTypeNoCtor(int marker) => Marker = marker;

        public int Marker { get; }

        public string TypeName => "fake-no-ctor";
        public string DisplayName => "FakeNoCtor";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });

        public NodeTypeDescriptor GetDescriptor() => new() { TypeName = "fake-no-ctor" };
    }

    private sealed class FakeExecutionLogger : IExecutionLogger
    {
        public List<string> Messages { get; } = [];

        public void LogInformation(string message, params object?[] args)
            => Messages.Add(args.Length > 0 ? $"{message}: {string.Join(", ", args)}" : message);

        public void LogWarning(string message, params object?[] args)
            => Messages.Add(args.Length > 0 ? $"{message}: {string.Join(", ", args)}" : message);

        public void LogError(Exception? exception, string message, params object?[] args)
            => Messages.Add(args.Length > 0 ? $"{message}: {string.Join(", ", args)}" : message);
    }

    private sealed class FakeContextFactory : INodeExecutionContextFactory
    {
        public bool WasCalled { get; private set; }

        public INodeType? ReceivedNodeInstance { get; private set; }

        public Task<NodeExecutionContext> CreateAsync(
            Workflow workflow,
            ExecutionRecord execution,
            NodeDefinition node,
            INodeType nodeInstance,
            IReadOnlyDictionary<string, DataBatch> inputs,
            IReadOnlyDictionary<string, DataBatch> successfulOutputs,
            IReadOnlyDictionary<string, DataBatch> latestBatches,
            int runIndex,
            CancellationToken cancellationToken,
            ICredentialAccessor? credentialAccessorOverride = null,
            IReadOnlyDictionary<string, object?>? extraGlobals = null,
            IDictionary<string, object?>? nodeContext = null)
        {
            WasCalled = true;
            ReceivedNodeInstance = nodeInstance;
            return Task.FromResult(new NodeExecutionContext
            {
                Workflow = workflow,
                ExecutionId = execution.Id,
                Node = node,
            });
        }
    }

    private static NodeDefinition BuildToolNode()
        => new()
        {
            Id = "tool-1",
            TypeName = "fake",
            Name = "Tool",
            Parameters = new Dictionary<string, object> { ["p"] = 1 },
            Ports = new List<PortInstance>(),
        };

    [Fact]
    public async Task CreateAsync_ActivatorFails_FallsBackToResolutionNodeType_AndLogsWarning()
    {
        var toolNode = BuildToolNode();
        var resolutionNodeType = new FakeNodeTypeNoCtor(7);
        var logger = new FakeExecutionLogger();
        var parentContext = new NodeExecutionContext
        {
            Workflow = new Workflow(),
            ExecutionId = Guid.NewGuid(),
            NestingDepth = 2,
        };
        var factory = new ToolContextFactory(parentContext, logger);
        var resolution = new ToolResolution(null, toolNode, resolutionNodeType, null);

        var result = await factory.CreateAsync(resolution, new DataBatch(), DateTime.UtcNow, CancellationToken.None);

        // 回退到解析出的节点类型实例（与 Activator 新建的不同引用）。
        Assert.Same(resolutionNodeType, result.ToolNodeInstance);
        Assert.Equal(3, result.Context.NestingDepth);
        Assert.Same(toolNode.Parameters, result.Context.RawParameters);
        Assert.Contains(logger.Messages, m => m.Contains("创建工具节点实例失败"));
    }

    [Fact]
    public async Task CreateAsync_WithContextFactory_UsesFactory_AndIncrementsNestingDepth()
    {
        var toolNode = BuildToolNode();
        var resolutionNodeType = new FakeNodeType();
        var contextFactory = new FakeContextFactory();
        var parentContext = new NodeExecutionContext
        {
            Workflow = new Workflow(),
            ExecutionId = Guid.NewGuid(),
            NestingDepth = 4,
            ContextFactory = contextFactory,
        };
        var factory = new ToolContextFactory(parentContext, null);
        var resolution = new ToolResolution(null, toolNode, resolutionNodeType, null);

        var result = await factory.CreateAsync(resolution, new DataBatch(), DateTime.UtcNow, CancellationToken.None);

        Assert.True(contextFactory.WasCalled);
        // Activator 成功创建了新实例，工厂收到的是该新实例（与 resolution 中的非同一引用）。
        Assert.NotNull(result.ToolNodeInstance);
        Assert.NotSame(resolutionNodeType, result.ToolNodeInstance);
        Assert.Same(contextFactory.ReceivedNodeInstance, result.ToolNodeInstance);
        Assert.Equal(5, result.Context.NestingDepth);
        Assert.Equal("tool-1", result.Context.Node.Id);
    }
}
