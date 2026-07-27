using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Credentials;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// 独立串行集合：确保本类测试执行期间无其他集合并发创建 JsEngine，
/// 使 JsEngine.CreateCallCount 计数在本次测试窗口内确定（用于验证引擎复用）。
/// </summary>
[CollectionDefinition("FactoryEngineReuse", DisableParallelization = true)]
public sealed class FactoryEngineReuseCollection;

[Collection("FactoryEngineReuse")]
public sealed class NodeExecutionContextFactoryTests
{
    private readonly NodeExecutionContextFactory _factory;
    private readonly StubNodeRegistry _registry;
    private readonly StubCredentialAccessor _credentialAccessor;

    public NodeExecutionContextFactoryTests()
    {
        _registry = new StubNodeRegistry([
            new NodeTypeDescriptor
            {
                TypeName = "testNode",
                DisplayName = "Test Node",
                Parameters =
                [
                    new ParameterDefinition { Name = "message", DisplayName = "Message", Required = true },
                    new ParameterDefinition { Name = "count", DisplayName = "Count", DefaultValue = 1 },
                    new ParameterDefinition { Name = "url", DisplayName = "URL" },
                ],
                Ports =
                [
                    new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                    new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
            new NodeTypeDescriptor
            {
                TypeName = "scriptNode",
                DisplayName = "Script Node",
                Parameters =
                [
                    new ParameterDefinition { Name = "expression", DisplayName = "Expression", Type = ParameterType.Script, Hint = PresentationHint.Expression },
                    new ParameterDefinition { Name = "code", DisplayName = "Code", Type = ParameterType.Script, Hint = PresentationHint.Script },
                    new ParameterDefinition { Name = "mapping", DisplayName = "Mapping", Type = ParameterType.Json, Hint = PresentationHint.Expression },
                ],
                Ports =
                [
                    new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                    new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
        ]);
        _credentialAccessor = new StubCredentialAccessor();
        _factory = new NodeExecutionContextFactory(
            _registry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            new ParameterResolver(
                NullLogger<ParameterResolver>.Instance,
                Options.Create(new JsEngineOptions()),
                new ScriptCache(Options.Create(new JsEngineOptions()))),
            _credentialAccessor,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            tokenService: new StubOAuth2TokenService());
    }

    [Fact]
    public async Task CreateAsync_ValidInputs_ReturnsContext()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = "Node1",
            TypeName = "testNode",
            Name = "Node1",
            Parameters = new Dictionary<string, object> { ["message"] = "hello" },
        };
        var nodeInstance = new TestNodeInstance();
        var inputs = new Dictionary<string, DataBatch>();
        var successfulOutputs = new Dictionary<string, DataBatch>();
        var latestBatches = new Dictionary<string, DataBatch>();

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            inputs, successfulOutputs, latestBatches, 0, ct);

        Assert.NotNull(context);
        Assert.Equal(workflow.Id, context.Workflow.Id);
        Assert.Equal(execution.Id, context.ExecutionId);
        Assert.Equal(node.Id, context.Node.Id);
        Assert.Equal(0, context.RunIndex);
        Assert.Same(inputs, context.Inputs);
    }

    [Fact]
    public async Task CreateAsync_InjectsNodeLocalExtraGlobals()
    {
        // 验证节点私有全局（如 $cursor）经 extraGlobals 注入并参与表达式求值，
        // 而工厂本身不感知该变量名（plan-004：节点私有变量不注册到顶层）。
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = "Node1_extraGlobals",
            TypeName = "testNode",
            Name = "Node1",
            Parameters = new Dictionary<string, object> { ["message"] = "\"page-\" + $cursor" },
        };
        var nodeInstance = new TestNodeInstance();
        var extraGlobals = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["$cursor"] = 3
        };

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0, ct, extraGlobals: extraGlobals);

        Assert.Equal("page-3", context.ResolvedParameters["message"]);
    }

    [Fact]
    public async Task CreateAsync_MergesDefaultParameters()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = "Node1_defaults",
            TypeName = "testNode",
            Name = "Node1",
            Parameters = new Dictionary<string, object> { ["message"] = "hello" },
        };
        var nodeInstance = new TestNodeInstance();

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0, ct);

        Assert.NotEmpty(context.RawParameters);
        Assert.True(context.RawParameters.ContainsKey("message"));
        Assert.True(context.RawParameters.ContainsKey("count"));
        Assert.Equal(1, context.RawParameters["count"]);
    }

    [Fact]
    public async Task CreateAsync_PreloadsOAuth2AccessToken_ForCredentialExpression()
    {
        // 验证 $credentials.<name>.accessToken 在表达式中可用（plan-004 阶段二）
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = "Node1_oauth2",
            TypeName = "testNode",
            Name = "Node1",
            Parameters = new Dictionary<string, object> { ["url"] = "$credentials.oauth2.accessToken" },
        };
        var nodeInstance = new TestNodeInstance();

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0, ct);

        Assert.Equal("tok-xxx", context.ResolvedParameters["url"]);
    }

    [Fact]
    public async Task CreateAsync_WithRunIndex_GetsCurrentInput()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = "Node1_runIndex",
            TypeName = "testNode",
            Name = "Node1",
        };
        var nodeInstance = new TestNodeInstance();
        var inputBatch = new DataBatch
        {
            Items =
            [
                new DataItem { Data = JsonNode.Parse("""{"value": "first"}""") },
                new DataItem { Data = JsonNode.Parse("""{"value": "second"}""") },
            ],
        };
        var inputs = new Dictionary<string, DataBatch> { ["input"] = inputBatch };

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            1, ct);

        Assert.Equal(1, context.RunIndex);
    }

    [Fact]
    public async Task CreateAsync_WithValidEmptyNodeType_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = "Bad",
            TypeName = "nonexistent",
            Name = "Bad",
        };
        var instance = new TestNodeInstance();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _factory.CreateAsync(workflow, execution, node, instance,
                new Dictionary<string, DataBatch>(),
                new Dictionary<string, DataBatch>(),
                new Dictionary<string, DataBatch>(),
                0, ct));
    }

    [Fact]
    public async Task CreateAsync_PreEvaluatesExpressionScriptParameter()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = "ScriptNode_expr",
            TypeName = "scriptNode",
            Name = "ScriptNode",
            Parameters = new Dictionary<string, object>
            {
                ["expression"] = new Script { Source = "1 + 1", ReturnType = ScriptReturnType.Number }
            }
        };
        var instance = new ScriptNodeInstance();

        var context = await _factory.CreateAsync(
            workflow, execution, node, instance,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0, ct);

        Assert.True(context.RawParameters.ContainsKey("expression"));
        var rawScript = Assert.IsType<Script>(context.RawParameters["expression"]);
        Assert.NotNull(rawScript.ResolvedValue);
        Assert.Equal(2, rawScript.ResolvedValue.GetValue<int>());

        Assert.True(context.ResolvedParameters.ContainsKey("expression"));
        var resolvedScript = Assert.IsType<Script>(context.ResolvedParameters["expression"]);
        Assert.NotNull(resolvedScript.ResolvedValue);
        Assert.Equal(2, resolvedScript.ResolvedValue.GetValue<int>());
    }

    [Fact]
    public async Task CreateAsync_DoesNotPreEvaluateScriptHintParameter()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = "ScriptNode_code",
            TypeName = "scriptNode",
            Name = "ScriptNode",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = new Script { Source = "return 42;", ReturnType = ScriptReturnType.Number }
            }
        };
        var instance = new ScriptNodeInstance();

        var context = await _factory.CreateAsync(
            workflow, execution, node, instance,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0, ct);

        var rawScript = Assert.IsType<Script>(context.RawParameters["code"]);
        Assert.Null(rawScript.ResolvedValue);
    }

    [Fact]
    public async Task CreateAsync_PreEvaluatesDictionaryOfScriptWithExpressionHint()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var mapping = new Dictionary<string, Script>
        {
            ["a"] = new Script { Source = "1 + 1", ReturnType = ScriptReturnType.Number }
        };
        var node = new NodeDefinition
        {
            Id = "ScriptNode_mapping",
            TypeName = "scriptNode",
            Name = "ScriptNode",
            Parameters = new Dictionary<string, object>
            {
                ["mapping"] = mapping
            }
        };
        var instance = new ScriptNodeInstance();

        var context = await _factory.CreateAsync(
            workflow, execution, node, instance,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0, ct);

        var rawDict = Assert.IsType<Dictionary<string, Script>>(context.RawParameters["mapping"]);
        Assert.NotNull(rawDict["a"].ResolvedValue);
        Assert.Equal(2, rawDict["a"].ResolvedValue!.GetValue<int>());
    }

    [Fact]
    public async Task CreateAsync_InjectsNodeContext_IntoGlobalVariables()
    {
        // 关键修正（Task 8）：节点级持久化上下文须注入 context.GlobalVariables（与运行期引擎同源），
        // 而非工厂临时 js/globals；且只要非 null 即注入（不要求非空）。
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition { Id = "Node1_ctx", TypeName = "testNode", Name = "Node1" };
        var nodeInstance = new TestNodeInstance();
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["counter"] = 0
        };

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0, ct, nodeContext: nodeContext);

        Assert.NotNull(context.NodeContext);
        // NodeContext 属性为同一实例引用。
        Assert.Same(nodeContext, context.NodeContext);
        Assert.NotNull(context.GlobalVariables);
        // 运行期全局变量表含 $nodeContext，且为同一实例。
        Assert.True(context.GlobalVariables.TryGetValue("$nodeContext", out var injected));
        Assert.Same(nodeContext, injected);
    }

    [Fact]
    public async Task CreateAsync_ReusesSingleManagedEngine_NotTempPerCall()
    {
        // Task 5-A：CreateAsync 应在参数预求值阶段复用 GetOrCreateEngine 返回的同一托管引擎，
        // 而非每次调用额外创建并销毁一个临时引擎。验证：从 CreateAsync 到后续 GetOrCreateEngine
        // 全程仅创建 1 个 JsEngine（修复前为 2：临时预求值引擎 + 节点执行体托管引擎）。
        JsEngine.ResetCreateCallCount();
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition { Id = "Node1", TypeName = "testNode", Name = "Node1" };
        var nodeInstance = new TestNodeInstance();

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0, ct);

        // 节点执行体随后经 GetOrCreateEngine 获取引擎：应为 CreateAsync 已创建的同一实例，不再新建。
        var engine = context.GetOrCreateEngine();
        Assert.NotNull(engine);

        Assert.Equal(1, JsEngine.CreateCallCount);

        context.ReleaseEngine();
    }

    [Fact]
    public async Task BodyExpression_CanReadWrite_NodeContext_WithDoubleRoundTrip()
    {
        // 验证点（Task 8 验收 #4）：在节点 body 表达式中可通过 $nodeContext 读写节点上下文；
        // 且 JS 写回数值为 double（Jint 数值约定）。
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition { Id = "Node1_body", TypeName = "testNode", Name = "Node1" };
        var nodeInstance = new TestNodeInstance();
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["counter"] = 3
        };

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0, ct, nodeContext: nodeContext);

        // 通过运行期引擎执行引用 $nodeContext 的脚本（与节点 body 脚本同一作用域注入路径）。
        var script = new Script
        {
            Source = "$nodeContext.counter = $nodeContext.counter + 1; return $nodeContext.counter;",
            ReturnType = ScriptReturnType.Number
        };

        var result = await script.ExecuteAsync(context, ct);

        Assert.True(result.Success, result.Error?.Message);
        // JS 写回为 double：3 + 1 → 4.0（同一实例被原地修改，非副本）。
        Assert.Equal(4.0, nodeContext["counter"]);
        Assert.IsType<double>(nodeContext["counter"]);
    }

    private sealed class TestNodeInstance : INodeType
    {
        public string TypeName => "testNode";
        public string DisplayName => "Test Node";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports =>
        [
            new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
        ];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct) =>
            Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class ScriptNodeInstance : INodeType
    {
        public string TypeName => "scriptNode";
        public string DisplayName => "Script Node";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports =>
        [
            new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
        ];
        public bool DefaultIsEntry => false;
        public Script Expression { get; set; } = Script.Empty;
        public Script Code { get; set; } = Script.Empty;
        public Dictionary<string, Script> Mapping { get; set; } = [];
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct) =>
            Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class StubNodeRegistry(IReadOnlyCollection<NodeTypeDescriptor> descriptors) : INodeRegistry
    {
        public void Register(INodeType nodeType) { }
        public INodeType Get(string typeName) => throw new InvalidOperationException();
        public bool TryGet(string typeName, out INodeType? nodeType) { nodeType = null; return false; }
        public IReadOnlyCollection<INodeType> GetAll() => [];
        public INodeType CreateInstance(string typeName) => throw new InvalidOperationException();
        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => descriptors;
        public NodeTypeDescriptor GetDescriptor(string typeName) =>
            descriptors.First(d => d.TypeName == typeName);
    }

    private sealed class StubCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken ct = default) =>
            Task.FromResult(new CredentialValue { Name = "stub", Type = "apiKey" });

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken ct = default)
        {
            if (string.Equals(name, "oauth2", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<CredentialValue?>(new CredentialValue
                {
                    Name = "oauth2",
                    Type = "oauth2",
                    Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tokenUrl"] = "http://example.com/token",
                        ["clientId"] = "cid",
                        ["clientSecret"] = "cs",
                        ["scope"] = "read"
                    }
                });
            }

            return Task.FromResult<CredentialValue?>(null);
        }
    }

    private sealed class StubOAuth2TokenService : IOAuth2TokenService
    {
        public Task<OAuth2TokenResponse> GetTokenAsync(OAuth2TokenRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OAuth2TokenResponse
            {
                AccessToken = "tok-xxx",
                TokenType = "Bearer",
                ExpiresIn = 3600
            });

        public Task<OAuth2TokenResponse> GetOrRefreshTokenAsync(string cacheKey, OAuth2TokenRequest request, CancellationToken cancellationToken = default) =>
            GetTokenAsync(request, cancellationToken);
    }
}
