using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// NoteNode 单元测试。note 为零端口节点，运行时被跳过（ENG2），此处仅验证防御性行为。
/// </summary>
public sealed class NoteNodeTests
{
    [Fact]
    public void Ports_IsEmpty()
    {
        var node = new NoteNode();

        Assert.Empty(node.Ports);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccess()
    {
        var node = new NoteNode();
        var context = await NodeTestContextFactory.BuildAsync(node);

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.NotNull(result.Output);
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public void TypeName_And_Metadata()
    {
        INodeType node = new NoteNode();

        Assert.Equal("note", node.TypeName);
        Assert.Equal("Note", node.DisplayName);
        Assert.Equal("Utility", node.Category);
        Assert.Equal("note", node.Icon);
        Assert.Equal(ExecutionMode.OnceForAll, node.ExecutionMode);
        Assert.False(node.DefaultIsEntry);
    }
}
