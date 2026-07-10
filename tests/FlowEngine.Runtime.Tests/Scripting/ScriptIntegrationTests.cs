using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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
using Xunit;

namespace FlowEngine.Runtime.Tests.Scripting;

/// <summary>
/// Script 类型端到端集成测试：验证 Hydrator + Factory 预求值 + ScriptCache 命中 + IfNode/FilterNode 正确执行。
/// </summary>
public sealed class ScriptIntegrationTests
{
    private static NodeExecutionContextFactory BuildFactory(ICredentialAccessor creds, params INodeType[] nodes)
    {
        var registry = new NodeRegistry(nodes, NullLogger<NodeRegistry>.Instance);
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
    public async Task IfNode_ExpressionScript_IsPreEvaluatedAndRoutesCorrectly()
    {
        var factory = BuildFactory(new NullCredentialAccessor(), new IfNode());
        var config = new Dictionary<string, object>
        {
            ["condition"] = new Script { Source = "$json.count > 5", ReturnType = ScriptReturnType.Bool }
        };
        var items = new List<DataItem>
        {
            new() { Data = JsonNode.Parse("{\"count\":10}"), Success = true, SourceIndex = 0 }
        };
        var inputs = new Dictionary<string, DataBatch>
        {
            [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
        };
        var nodeDef = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "if",
            Name = "if1",
            Parameters = config
        };

        var nodeInstance = new IfNode();
        var context = await factory.CreateAsync(
            new Workflow { Id = Guid.NewGuid(), Name = "t" },
            new ExecutionRecord { Id = Guid.NewGuid() },
            nodeDef,
            nodeInstance,
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0,
            CancellationToken.None);

        Assert.Equal("$json.count > 5", nodeInstance.Condition.Source);
        Assert.NotNull(nodeInstance.Condition.ResolvedValue);
        Assert.True(nodeInstance.Condition.GetResult<bool>());

        var result = await nodeInstance.ExecuteAsync(context, CancellationToken.None);
        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(0, result.BranchIndex);
    }

    [Fact]
    public async Task FilterNode_Script_IsEvaluatedPerItemUsingCache()
    {
        var factory = BuildFactory(new NullCredentialAccessor(), new FilterNode());
        var config = new Dictionary<string, object>
        {
            ["condition"] = new Script { Source = "$json.value > 1", ReturnType = ScriptReturnType.Bool }
        };
        var items = new List<DataItem>
        {
            new() { Data = JsonNode.Parse("{\"value\":1}"), Success = true, SourceIndex = 0 },
            new() { Data = JsonNode.Parse("{\"value\":2}"), Success = true, SourceIndex = 1 },
            new() { Data = JsonNode.Parse("{\"value\":3}"), Success = true, SourceIndex = 2 }
        };
        var inputs = new Dictionary<string, DataBatch>
        {
            [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
        };
        var nodeDef = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "filter",
            Name = "filter1",
            Parameters = config
        };

        var context = await factory.CreateAsync(
            new Workflow { Id = Guid.NewGuid(), Name = "t" },
            new ExecutionRecord { Id = Guid.NewGuid() },
            nodeDef,
            new FilterNode(),
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0,
            CancellationToken.None);

        Assert.NotNull(context.ScriptCache);

        var node = new FilterNode { Condition = new Script { Source = "$json.value > 1", ReturnType = ScriptReturnType.Bool } };
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
    }

    [Fact]
    public async Task ScriptCache_Hits_ForSameSource()
    {
        var cache = new ScriptCache(Options.Create(new JsEngineOptions()));
        var script = new Script { Source = "$json.value * 2", ReturnType = ScriptReturnType.Number };

        var prepared1 = cache.GetOrPrepare(script);
        var prepared2 = cache.GetOrPrepare(script);

        Assert.Equal(prepared1.CacheKey, prepared2.CacheKey);
    }

    [Fact]
    public async Task HttpRequestNode_UrlExpression_IsPreEvaluated()
    {
        var factory = BuildFactory(new NullCredentialAccessor(), new HttpRequestNode());
        var config = new Dictionary<string, object>
        {
            ["url"] = new Script { Source = "$json.base + '/api'", ReturnType = ScriptReturnType.String }
        };
        var items = new List<DataItem>
        {
            new() { Data = JsonNode.Parse("{\"base\":\"http://example.com\"}"), Success = true, SourceIndex = 0 }
        };
        var inputs = new Dictionary<string, DataBatch>
        {
            [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
        };
        var nodeDef = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "httpRequest",
            Name = "http1",
            Parameters = config
        };

        var nodeInstance = new HttpRequestNode();
        var context = await factory.CreateAsync(
            new Workflow { Id = Guid.NewGuid(), Name = "t" },
            new ExecutionRecord { Id = Guid.NewGuid() },
            nodeDef,
            nodeInstance,
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0,
            CancellationToken.None);

        Assert.Equal("$json.base + '/api'", nodeInstance.Url.Source);
        Assert.NotNull(nodeInstance.Url.ResolvedValue);
        Assert.Equal("http://example.com/api", nodeInstance.Url.GetResult<string>());
        Assert.True(context.ResolvedParameters["url"] is Script resolved && resolved.GetResult<string>() == "http://example.com/api");
    }

    [Fact]
    public async Task HttpToolNode_UrlExpression_IsPreEvaluated()
    {
        var factory = BuildFactory(new NullCredentialAccessor(), new HttpToolNode());
        var config = new Dictionary<string, object>
        {
            ["url"] = new Script { Source = "'https://api.example.com/' + $json.segment", ReturnType = ScriptReturnType.String }
        };
        var items = new List<DataItem>
        {
            new() { Data = JsonNode.Parse("{\"segment\":\"users\"}"), Success = true, SourceIndex = 0 }
        };
        var inputs = new Dictionary<string, DataBatch>
        {
            [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
        };
        var nodeDef = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "httpTool",
            Name = "httpTool1",
            Parameters = config
        };

        var nodeInstance = new HttpToolNode();
        var context = await factory.CreateAsync(
            new Workflow { Id = Guid.NewGuid(), Name = "t" },
            new ExecutionRecord { Id = Guid.NewGuid() },
            nodeDef,
            nodeInstance,
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0,
            CancellationToken.None);

        Assert.Equal("'https://api.example.com/' + $json.segment", nodeInstance.Url.Source);
        Assert.NotNull(nodeInstance.Url.ResolvedValue);
        Assert.Equal("https://api.example.com/users", nodeInstance.Url.GetResult<string>());
        Assert.True(context.ResolvedParameters["url"] is Script resolved && resolved.GetResult<string>() == "https://api.example.com/users");
    }

    [Fact]
    public async Task SwitchNode_Expression_IsPreEvaluatedAndRoutesCorrectly()
    {
        var factory = BuildFactory(new NullCredentialAccessor(), new SwitchNode());
        var config = new Dictionary<string, object>
        {
            ["expression"] = new Script { Source = "$json.category", ReturnType = ScriptReturnType.String },
            ["cases"] = "[{\"name\":\"a\",\"label\":\"A\",\"value\":\"a\"},{\"name\":\"b\",\"label\":\"B\",\"value\":\"b\"}]"
        };
        var items = new List<DataItem>
        {
            new() { Data = JsonNode.Parse("{\"category\":\"b\"}"), Success = true, SourceIndex = 0 }
        };
        var inputs = new Dictionary<string, DataBatch>
        {
            [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
        };
        var nodeDef = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "switch",
            Name = "switch1",
            Parameters = config
        };

        var nodeInstance = new SwitchNode();
        var context = await factory.CreateAsync(
            new Workflow { Id = Guid.NewGuid(), Name = "t" },
            new ExecutionRecord { Id = Guid.NewGuid() },
            nodeDef,
            nodeInstance,
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0,
            CancellationToken.None);

        Assert.Equal("$json.category", nodeInstance.Expression.Source);
        Assert.NotNull(nodeInstance.Expression.ResolvedValue);
        Assert.Equal("b", nodeInstance.Expression.GetResult<string>());

        var result = await nodeInstance.ExecuteAsync(context, CancellationToken.None);
        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(1, result.BranchIndex);
    }

    private sealed class NullCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue>(null!);

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }
}
