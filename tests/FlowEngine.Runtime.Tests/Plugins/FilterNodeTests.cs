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
/// FilterNode 单元测试：验证 Condition 改为逐项走统一表达式引擎（JsEngine + GlobalVariables + $json/$input），
/// 支持裸式 `$json.field` 表达式，并保留空条件时保留全部 item 的行为。
/// 对应 review 发现 A4（FilterNode 的 `{{ $json }}` mustache 解析器已删除，改走逐项 JsEngine）。
/// </summary>
public sealed class FilterNodeTests
{
    private static NodeExecutionContext BuildContext(string condition, List<DataItem> items)
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
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "t" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition { Id = Guid.NewGuid(), TypeName = "filter", Name = "f1", Parameters = config },
            Inputs = inputs,
            RawParameters = config,
            ResolvedParameters = config,
            Credentials = new NullCredentialAccessor(),
            CancellationToken = CancellationToken.None,
            NodeRegistry = registry,
            ContextFactory = factory
        };
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
        var context = BuildContext("$json.value > 1", items);

        var result = await new FilterNode { Condition = "$json.value > 1" }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCondition_KeepsAll()
    {
        var items = BuildItems(1, 2, 3);
        var context = BuildContext("", items);

        var result = await new FilterNode { Condition = "" }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(3, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ExpressionCanAccessInputContext()
    {
        // 验证统一 ExecutionScope 注入后 $input.context 可用
        // （修复前节点自行构造 InputContainer 时未传入 inputContext，表达式为 null 会报错丢 item）。
        var items = BuildItems(1);
        var context = BuildContext("$json.value > 0", items);

        var result = await new FilterNode { Condition = "$input.context.nodeName === 'f1'" }
            .ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
    }

    private sealed class NullCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue>(null!);

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }
}
