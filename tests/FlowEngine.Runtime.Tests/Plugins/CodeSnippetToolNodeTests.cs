using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Tests.Plugins;

public class CodeSnippetToolNodeTests
{
    private readonly CodeSnippetToolNode _node = new();

    [Fact]
    public async Task Execute_MissingCode_ReturnsError()
    {
        var node = new CodeSnippetToolNode { Code = "" };
        var context = CreateContext(new JsonObject());

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MissingCode", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_SimpleCode_ReturnsResult()
    {
        var node = new CodeSnippetToolNode
        {
            Code = "return 42;"
        };
        var context = CreateContext(new JsonObject());

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);
        Assert.Equal(42, data!.GetValue<int>());
    }

    [Fact]
    public async Task Execute_CodeWithInput_AccessesInput()
    {
        var node = new CodeSnippetToolNode
        {
            Code = "return input;"
        };
        var input = new JsonObject { ["name"] = "Alice" };
        var context = CreateContext(input);

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
        Assert.NotEmpty(result.Output.Items);
        Assert.NotNull(result.Output.Items[0].Data);
    }

    [Fact]
    public async Task Execute_CodeReturningObject_ReturnsJsonObject()
    {
        var node = new CodeSnippetToolNode
        {
            Code = "return { message: 'ok', count: 5 };"
        };
        var context = CreateContext(new JsonObject());

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
        var json = result.Output.Items[0].Data?.ToJsonString();
        Assert.Contains("\"message\":\"ok\"", json!);
        Assert.Contains("\"count\":5", json);
    }

    [Fact]
    public async Task Execute_ScriptError_ReturnsCodeError()
    {
        var node = new CodeSnippetToolNode
        {
            Code = "throw new Error('test error');"
        };
        var context = CreateContext(new JsonObject());

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("ScriptError", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_FactoryHydration_CodeParameterAsScript_Works()
    {
        var factory = BuildFactory();
        var node = new CodeSnippetToolNode();
        var config = new Dictionary<string, object>
        {
            ["code"] = "return input.value * 2;"
        };
        var context = await BuildContextAsync(factory, node, config, JsonNode.Parse("{\"value\":21}"));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);
        Assert.Equal(42, data!.GetValue<int>());
    }

    private static NodeExecutionContextFactory BuildFactory() =>
        new(
            new NodeRegistry(new List<INodeType> { new CodeSnippetToolNode() }, NullLogger<NodeRegistry>.Instance),
            new ScriptCache(Options.Create(new JsEngineOptions())),
            new ParameterResolver(
                NullLogger<ParameterResolver>.Instance,
                Options.Create(new JsEngineOptions()),
                new ScriptCache(Options.Create(new JsEngineOptions()))),
            new NullCredentialAccessor(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static async Task<NodeExecutionContext> BuildContextAsync(
        NodeExecutionContextFactory factory,
        CodeSnippetToolNode nodeInstance,
        Dictionary<string, object> config,
        JsonNode? inputData)
    {
        var items = inputData is null
            ? new List<DataItem>()
            : new List<DataItem> { new() { Data = inputData, Success = true, SourceIndex = 0 } };
        var inputs = new Dictionary<string, DataBatch>
        {
            [FlowConstants.PortNames.Input] = new() { Items = items }
        };
        var nodeDef = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "codeTool",
            Name = "codeTool1",
            Parameters = config
        };
        return await factory.CreateAsync(
            new Workflow { Id = Guid.NewGuid(), Name = "t" },
            new ExecutionRecord { Id = Guid.NewGuid() },
            nodeDef,
            nodeInstance,
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0,
            CancellationToken.None);
    }

    private sealed class NullCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue>(null!);

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }

    private static NodeExecutionContext CreateContext(JsonObject inputPayload)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "codeSnippetTool",
                Name = "Test CodeSnippet",
                Parameters = [],
                Ports = [],
                ErrorStrategy = ErrorStrategy.Terminate
            },
            ExecutionId = Guid.NewGuid(),
            Inputs = new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new()
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = inputPayload,
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            CancellationToken = CancellationToken.None
        };
    }
}
