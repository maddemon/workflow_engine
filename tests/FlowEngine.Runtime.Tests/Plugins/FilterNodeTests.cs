using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;
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
/// FilterNode 单元测试：验证 Condition 改为 <see cref="Script"/> 类型后，
/// 逐项走 IScriptCache + PreparedScriptSession 求值，支持裸式 <c>$json.field</c> 表达式，
/// 脚本错误时向上冒泡，空条件时保留全部 item。
/// </summary>
public sealed class FilterNodeTests
{
    private static async Task<NodeExecutionContext> BuildContextAsync(string condition, List<DataItem> items)
    {
        var config = new Dictionary<string, object> { ["condition"] = condition };
        var inputs = new Dictionary<string, DataBatch>
        {
            [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
        };
        var registry = new NodeRegistry(new List<INodeType> { new FilterNode() }, NullLogger<NodeRegistry>.Instance);
        var factory = new NodeExecutionContextFactory(
            registry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            new ParameterResolver(NullLogger<ParameterResolver>.Instance),
            new NullCredentialAccessor(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var nodeDef = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "filter",
            Name = "f1",
            Parameters = config
        };
        return await factory.CreateAsync(
            new Workflow { Id = Guid.NewGuid(), Name = "t" },
            new ExecutionRecord { Id = Guid.NewGuid() },
            nodeDef,
            new FilterNode(),
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0,
            CancellationToken.None).ConfigureAwait(false);
    }

    private static List<DataItem> BuildItems(params int[] values)
    {
        var items = new List<DataItem>();
        for (var i = 0; i < values.Length; i++)
        {
            items.Add(new DataItem
            {
                Data = JsonNode.Parse($"{{\"value\":{values[i]}}}"),
                Success = true,
                SourceIndex = i
            });
        }

        return items;
    }

    [Fact]
    public async Task ExecuteAsync_KeepsItemsMatchingExpression()
    {
        var items = BuildItems(1, 2, 3);
        var context = await BuildContextAsync("$json.value > 1", items);

        var result = await new FilterNode { Condition = new Script { Source = "$json.value > 1", ReturnType = ScriptReturnType.Bool } }
            .ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCondition_KeepsAll()
    {
        var items = BuildItems(1, 2, 3);
        var context = await BuildContextAsync("", items);

        var result = await new FilterNode { Condition = Script.Empty }
            .ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(3, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ExpressionCanAccessInputContext()
    {
        // 验证统一 ExecutionScope 注入后 $input.context 可用
        var items = BuildItems(1);
        var context = await BuildContextAsync("$json.value > 0", items);

        var result = await new FilterNode { Condition = new Script { Source = "$input.context.nodeName === 'f1'", ReturnType = ScriptReturnType.Bool } }
            .ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_PerItemVariables_AreIsolated()
    {
        var items = new List<DataItem>
        {
            new() { Data = JsonNode.Parse("{\"value\":1}"), Success = true, SourceIndex = 0 },
            new() { Data = JsonNode.Parse("{\"value\":2}"), Success = true, SourceIndex = 1 }
        };
        var context = await BuildContextAsync("$json.value === $itemIndex + 1", items);

        var result = await new FilterNode { Condition = new Script { Source = "$json.value === $itemIndex + 1", ReturnType = ScriptReturnType.Bool } }
            .ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ScriptError_ThrowsScriptErrorException()
    {
        var items = BuildItems(1);
        var context = await BuildContextAsync("$json.value === ", items);

        await Assert.ThrowsAsync<ScriptErrorException>(() =>
            new FilterNode { Condition = new Script { Source = "$json.value === ", ReturnType = ScriptReturnType.Bool } }
                .ExecuteAsync(context, CancellationToken.None));
    }

    private sealed class NullCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue>(null!);

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }
}
