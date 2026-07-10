using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// IfNode 单元测试：验证 Condition 经工厂 ParameterResolver 求值（统一表达式引擎）后，
/// 按结果路由到 True/False 分支，且缺失 condition 时显式报错。
/// 对应 review 发现 A4（IfNode 自写解析器已删除，改走统一表达式引擎）。
/// </summary>
public sealed class IfNodeTests
{
    private static NodeExecutionContextFactory BuildFactory(ICredentialAccessor creds) =>
        new(
            new NodeRegistry(new List<INodeType> { new IfNode() }, NullLogger<NodeRegistry>.Instance),
            new ScriptCache(Options.Create(new JsEngineOptions())),
            new ParameterResolver(NullLogger<ParameterResolver>.Instance),
            creds,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static async Task<NodeExecutionContext> BuildContextAsync(
        NodeExecutionContextFactory factory,
        Dictionary<string, object> config,
        JsonNode? inputData)
    {
        var items = inputData is null
            ? new List<DataItem>()
            : new List<DataItem> { new() { Data = inputData, Success = true, SourceIndex = 0 } };
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
        return await factory.CreateAsync(
            new Workflow { Id = Guid.NewGuid(), Name = "t" },
            new ExecutionRecord { Id = Guid.NewGuid() },
            nodeDef,
            new IfNode(),
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0,
            CancellationToken.None).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExecuteAsync_ConditionTrue_RoutesToTrueBranch()
    {
        var factory = BuildFactory(new NullCredentialAccessor());
        var config = new Dictionary<string, object> { ["condition"] = "$json.status === 'active'" };
        var context = await BuildContextAsync(factory, config, JsonNode.Parse("{\"status\":\"active\"}"));

        var result = await new IfNode().ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(0, result.BranchIndex); // True 端口
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_ConditionFalse_RoutesToFalseBranch()
    {
        var factory = BuildFactory(new NullCredentialAccessor());
        var config = new Dictionary<string, object> { ["condition"] = "$json.status === 'active'" };
        var context = await BuildContextAsync(factory, config, JsonNode.Parse("{\"status\":\"inactive\"}"));

        var result = await new IfNode().ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(1, result.BranchIndex); // False 端口
    }

    [Fact]
    public async Task ExecuteAsync_MissingCondition_ReturnsError()
    {
        var factory = BuildFactory(new NullCredentialAccessor());
        var config = new Dictionary<string, object>(); // 无 condition 键
        var context = await BuildContextAsync(factory, config, null);

        var result = await new IfNode().ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal("MissingCondition", result.Error!.Code);
    }

    private sealed class NullCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue>(null!);

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }
}
