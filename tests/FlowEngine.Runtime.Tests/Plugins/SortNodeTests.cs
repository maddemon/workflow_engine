using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// SortNode 单元测试，重点验证异构类型排序不崩溃。
/// </summary>
public sealed class SortNodeTests
{
    private static NodeExecutionContext CreateContext(DataBatch input)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "sort1",
                TypeName = "sort",
                Name = "sort1"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            }
        };
    }

    private static DataItem Item(string? value, int index)
    {
        return new DataItem
        {
            Data = value is null ? null : new JsonObject { ["val"] = value },
            Success = true,
            SourceIndex = index
        };
    }

    private static string? GetValue(DataItem item)
    {
        return item.Data?["val"]?.GetValue<string>();
    }

    [Fact]
    public async Task ExecuteAsync_HeterogeneousTypes_DoesNotCrash()
    {
        var input = new DataBatch
        {
            Items = [Item("10", 0), Item("abc", 1), Item("2", 2), Item("xyz", 3)]
        };

        var node = new SortNode
        {
            SortFields = [new SortField { FieldName = "val", Direction = SortDirection.Asc }]
        };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(4, result.Output.Items.Count);
        // Numeric values sort numerically (2 < 10), string values sort alphabetically (abc < xyz)
        // Mixed: numerics first (both have numericValue), then strings
        Assert.Equal("2", GetValue(result.Output.Items[0]));
        Assert.Equal("10", GetValue(result.Output.Items[1]));
        Assert.Equal("abc", GetValue(result.Output.Items[2]));
        Assert.Equal("xyz", GetValue(result.Output.Items[3]));
    }

    [Fact]
    public async Task ExecuteAsync_NumericSort()
    {
        var input = new DataBatch
        {
            Items = [Item("10", 0), Item("2", 1), Item("1", 2)]
        };

        var node = new SortNode
        {
            SortFields = [new SortField { FieldName = "val", Direction = SortDirection.Asc }]
        };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("1", GetValue(result.Output.Items[0]));
        Assert.Equal("2", GetValue(result.Output.Items[1]));
        Assert.Equal("10", GetValue(result.Output.Items[2]));
    }

    [Fact]
    public async Task ExecuteAsync_StringSort()
    {
        var input = new DataBatch
        {
            Items = [Item("banana", 0), Item("apple", 1), Item("cherry", 2)]
        };

        var node = new SortNode
        {
            SortFields = [new SortField { FieldName = "val", Direction = SortDirection.Asc }]
        };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("apple", GetValue(result.Output.Items[0]));
        Assert.Equal("banana", GetValue(result.Output.Items[1]));
        Assert.Equal("cherry", GetValue(result.Output.Items[2]));
    }

    [Fact]
    public async Task ExecuteAsync_NullValues_DoesNotCrash()
    {
        var input = new DataBatch
        {
            Items = [Item(null, 0), Item("value", 1), Item(null, 2)]
        };

        var node = new SortNode
        {
            SortFields = [new SortField { FieldName = "val", Direction = SortDirection.Asc }]
        };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(3, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_DescendingSort()
    {
        var input = new DataBatch
        {
            Items = [Item("1", 0), Item("3", 1), Item("2", 2)]
        };

        var node = new SortNode
        {
            SortFields = [new SortField { FieldName = "val", Direction = SortDirection.Desc }]
        };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("3", GetValue(result.Output.Items[0]));
        Assert.Equal("2", GetValue(result.Output.Items[1]));
        Assert.Equal("1", GetValue(result.Output.Items[2]));
    }
}
