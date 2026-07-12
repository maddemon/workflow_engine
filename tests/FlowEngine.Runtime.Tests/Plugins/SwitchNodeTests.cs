using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Tests.Plugins;

public class SwitchNodeTests
{
    [Fact]
    public async Task Execute_ResolvedValue_RoutesToMatchingCase()
    {
        var node = new SwitchNode
        {
            Expression = new Script
            {
                Source = "$json.category",
                Language = ScriptLanguage.JavaScript,
                ReturnType = ScriptReturnType.String
            }.WithResolvedValue(JsonValue.Create("b")),
            Cases =
            [
                new SwitchCase { Name = "a", Label = "A", Value = "a" },
                new SwitchCase { Name = "b", Label = "B", Value = "b" }
            ]
        };

        var context = CreateContext(JsonNode.Parse("{\"category\":\"b\"}")!);

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(1, result.BranchIndex);
    }

    [Fact]
    public async Task Execute_ResolvedExpression_RoutesToMatchingCase()
    {
        var node = new SwitchNode
        {
            Expression = new Script
            {
                Source = "$json.category",
                Language = ScriptLanguage.JavaScript,
                ReturnType = ScriptReturnType.String
            }.WithResolvedValue(JsonValue.Create("a")),
            Cases =
            [
                new SwitchCase { Name = "a", Label = "A", Value = "a" },
                new SwitchCase { Name = "b", Label = "B", Value = "b" }
            ]
        };

        var context = CreateContext(JsonNode.Parse("{\"category\":\"a\"}")!);

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(0, result.BranchIndex);
    }

    [Fact]
    public async Task Execute_NoMatch_RoutesToDefault()
    {
        var node = new SwitchNode
        {
            Expression = new Script
            {
                Source = "$json.category",
                Language = ScriptLanguage.JavaScript,
                ReturnType = ScriptReturnType.String
            }.WithResolvedValue(JsonValue.Create("z")),
            Cases =
            [
                new SwitchCase { Name = "a", Label = "A", Value = "a" }
            ]
        };

        var context = CreateContext(JsonNode.Parse("{\"category\":\"z\"}")!);

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(1, result.BranchIndex); // default port index == Cases.Count
    }

    [Fact]
    public async Task Execute_FactoryPreEvaluation_SyntaxError_ThrowsScriptErrorException()
    {
        var factory = BuildFactory();
        var node = new SwitchNode();
        var config = new Dictionary<string, object>
        {
            ["expression"] = "$json.invalid === ",
            ["cases"] = "[{\"name\":\"a\",\"label\":\"A\",\"value\":\"a\"}]"
        };

        var ex = await Assert.ThrowsAsync<ScriptErrorException>(() =>
            BuildContextAsync(factory, node, config, JsonNode.Parse("{\"invalid\":\"x\"}")));

        Assert.Contains("预求值失败", ex.Message);
    }

    [Fact]
    public async Task Execute_FactoryPreEvaluation_RoutesToMatchingCase()
    {
        var factory = BuildFactory();
        var node = new SwitchNode();
        var config = new Dictionary<string, object>
        {
            ["expression"] = "$json.category",
            ["cases"] = "[{\"name\":\"a\",\"label\":\"A\",\"value\":\"a\"},{\"name\":\"b\",\"label\":\"B\",\"value\":\"b\"}]"
        };
        var context = await BuildContextAsync(factory, node, config, JsonNode.Parse("{\"category\":\"b\"}"));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(1, result.BranchIndex);
    }

    private static NodeExecutionContextFactory BuildFactory() =>
        new(
            new NodeRegistry(new List<INodeType> { new SwitchNode() }, NullLogger<NodeRegistry>.Instance),
            new ScriptCache(Options.Create(new JsEngineOptions())),
            new ParameterResolver(
                NullLogger<ParameterResolver>.Instance,
                Options.Create(new JsEngineOptions()),
                new ScriptCache(Options.Create(new JsEngineOptions()))),
            new NullCredentialAccessor(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static async Task<NodeExecutionContext> BuildContextAsync(
        NodeExecutionContextFactory factory,
        SwitchNode nodeInstance,
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
            Id = "switch1",
            TypeName = "switch",
            Name = "switch1",
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

    private static NodeExecutionContext CreateContext(JsonNode inputPayload)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = "Test Switch",
                TypeName = "switch",
                Name = "Test Switch",
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
            GlobalVariables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["$json"] = inputPayload,
                ["input"] = inputPayload
            },
            CancellationToken = CancellationToken.None
        };
    }
}
