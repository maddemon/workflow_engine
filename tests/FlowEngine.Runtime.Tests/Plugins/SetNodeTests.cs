using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

public sealed class SetNodeTests
{
    [Fact]
    public async Task ExecuteAsync_StaticStringValue_WritesLiteral()
    {
        var node = new SetNode
        {
            Fields =
            [
                new SetField
                {
                    Name = "status",
                    Value = new Script { Source = "active", ReturnType = ScriptReturnType.String }
                }
            ]
        };

        var context = CreateContext(new JsonObject { ["id"] = 1 });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("active", data["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_StaticBoolValue_WritesBoolean()
    {
        var node = new SetNode
        {
            Fields =
            [
                new SetField
                {
                    Name = "flag",
                    Value = new Script { Source = "true", ReturnType = ScriptReturnType.Bool }
                }
            ]
        };

        var context = CreateContext(new JsonObject { ["id"] = 1 });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.True(data["flag"]?.GetValue<bool>());
    }

    [Fact]
    public async Task ExecuteAsync_ExpressionValue_EvaluatesJsonPath()
    {
        var node = new SetNode
        {
            Fields =
            [
                new SetField
                {
                    Name = "uid",
                    Value = new Script { Source = "$json.userid", ReturnType = ScriptReturnType.String }
                }
            ]
        };

        var context = CreateContext(new JsonObject { ["userid"] = "u-123" });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("u-123", data["uid"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ExpressionConcat_CombinesFields()
    {
        var node = new SetNode
        {
            Fields =
            [
                new SetField
                {
                    Name = "label",
                    Value = new Script { Source = "$json.name + ' (' + $json.dept + ')'", ReturnType = ScriptReturnType.String }
                }
            ]
        };

        var context = CreateContext(new JsonObject { ["name"] = "Alice", ["dept"] = "Eng" });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("Alice (Eng)", data["label"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_MixedStaticAndExpression_WritesBoth()
    {
        var node = new SetNode
        {
            Fields =
            [
                new SetField { Name = "status", Value = new Script { Source = "active", ReturnType = ScriptReturnType.String } },
                new SetField { Name = "uid", Value = new Script { Source = "$json.userid", ReturnType = ScriptReturnType.String } }
            ]
        };

        var context = CreateContext(new JsonObject { ["userid"] = "u-9" });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("active", data["status"]?.GetValue<string>());
        Assert.Equal("u-9", data["uid"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_NestedPath_WriteNestedField()
    {
        var node = new SetNode
        {
            Fields =
            [
                new SetField
                {
                    Name = "addr.city",
                    Value = new Script { Source = "$json.city", ReturnType = ScriptReturnType.String }
                }
            ]
        };

        var context = CreateContext(new JsonObject { ["city"] = "Shanghai" });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("Shanghai", data["addr"]?["city"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_EmptySource_WritesEmptyString()
    {
        var node = new SetNode
        {
            Fields =
            [
                new SetField { Name = "x", Value = Script.Empty }
            ]
        };

        var context = CreateContext(new JsonObject { ["id"] = 1 });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal(string.Empty, data["x"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_BackwardCompatible_PlainStringJson_DeserializedAsLiteral()
    {
        // 旧工作流存储 "value": "hello"（纯字符串）经 ScriptJsonConverter 简写为 Script，应作为字面量
        const string json = """{"fields":[{"name":"greeting","value":"hello"}]}""";
        var node = System.Text.Json.JsonSerializer.Deserialize<SetNode>(json, JsonDefaults.Options);
        Assert.NotNull(node);
        Assert.Single(node.Fields);
        Assert.Equal("hello", node.Fields[0].Value.Source);

        var context = CreateContext(new JsonObject { ["id"] = 1 });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("hello", data["greeting"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_InvalidExpression_LogsWarningAndFallsBackToLiteral()
    {
        var logger = new FakeExecutionLogger();
        var node = new SetNode
        {
            Fields =
            [
                new SetField
                {
                    Name = "fallback",
                    Value = new Script { Source = "$json..invalid", ReturnType = ScriptReturnType.String }
                }
            ]
        };

        var context = CreateContext(new JsonObject { ["id"] = 1 }, logger);
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("$json..invalid", data["fallback"]?.GetValue<string>());
        Assert.Single(logger.Warnings);
        Assert.Contains("SetNode", logger.Warnings[0].Message);
        Assert.Contains("$json..invalid", logger.Warnings[0].Args.Select(a => a?.ToString()));
    }

    private static NodeExecutionContext CreateContext(JsonObject inputData, IExecutionLogger? logger = null)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = "Set",
                TypeName = "set",
                Name = "Set",
                Parameters = new Dictionary<string, object>(),
                Ports = []
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
                            Data = inputData,
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = new NullAccessor(),
            ScriptCache = new ScriptCache(Options.Create(new JsEngineOptions())),
            EngineOptions = new JsEngineOptions(),
            Logger = logger ?? NullExecutionLogger.Instance,
            CancellationToken = CancellationToken.None
        };
    }

    private sealed class NullAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken ct = default) =>
            Task.FromResult<CredentialValue>(null!);

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult<CredentialValue?>(null);
    }

    private sealed class NullExecutionLogger : IExecutionLogger
    {
        public static readonly NullExecutionLogger Instance = new();

        public void LogInformation(string message, params object?[] args) { }

        public void LogWarning(string message, params object?[] args) { }

        public void LogError(Exception? exception, string message, params object?[] args) { }
    }

    private sealed class FakeExecutionLogger : IExecutionLogger
    {
        public List<(string Message, object?[] Args)> Warnings { get; } = [];

        public void LogInformation(string message, params object?[] args) { }

        public void LogWarning(string message, params object?[] args)
        {
            Warnings.Add((message, args));
        }

        public void LogError(Exception? exception, string message, params object?[] args) { }
    }
}
