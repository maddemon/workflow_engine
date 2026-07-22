using System.Collections.Generic;
using FlowEngine.Core.Entities;
using Xunit;

namespace FlowEngine.Runtime.Tests;

/// <summary>
/// <see cref="NodeContextExtensions"/> 覆盖测试：强类型 <c>Get/Set/TryGet/GetOrAdd&lt;T&gt;</c>
/// （<c>where T : class</c>）与非泛型 <c>GetValue/SetValue</c>（值类型 int/double/bool）。
/// </summary>
public sealed class NodeContextExtensionsTests
{
    private static IDictionary<string, object?> NewContext()
        => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Set_Get_StrongTypeRoundTrip()
    {
        var ctx = NewContext();
        var list = new List<DataItem>();

        ctx.Set("list", list);
        ctx.Set("name", "loop1");

        Assert.Same(list, ctx.Get<List<DataItem>>("list"));
        Assert.Equal("loop1", ctx.Get<string>("name"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsDefault()
    {
        var ctx = NewContext();

        Assert.Null(ctx.Get<List<DataItem>>("absent"));
        Assert.Null(ctx.Get<string>("absent"));
    }

    [Fact]
    public void TryGet_Success_ReturnsTrueAndValue()
    {
        var ctx = NewContext();
        ctx.Set("name", "loop1");

        Assert.True(ctx.TryGet<string>("name", out var value));
        Assert.Equal("loop1", value);
    }

    [Fact]
    public void TryGet_WrongType_ReturnsFalse()
    {
        var ctx = NewContext();
        ctx.Set("count", 5); // 值类型，Get<T> 约束为 class 不应命中

        Assert.False(ctx.TryGet<string>("count", out _));
    }

    [Fact]
    public void GetOrAdd_AddsOnce_ThenReuses()
    {
        var ctx = NewContext();

        var first = ctx.GetOrAdd("state", () => new List<DataItem>());
        var second = ctx.GetOrAdd("state", () => new List<DataItem>());

        Assert.Same(first, second);
        Assert.Single(ctx); // 仅添加一次
    }

    [Fact]
    public void GetValue_SetValue_IntRoundTrip()
    {
        var ctx = NewContext();
        ctx.SetValue("count", 7);

        Assert.Equal(7, ctx.GetValue("count"));
        Assert.IsType<int>(ctx.GetValue("count"));
    }

    [Fact]
    public void GetValue_SetValue_DoubleRoundTrip()
    {
        var ctx = NewContext();
        ctx.SetValue("ratio", 2.5);

        Assert.Equal(2.5, ctx.GetValue("ratio"));
        Assert.IsType<double>(ctx.GetValue("ratio"));
    }

    [Fact]
    public void GetValue_SetValue_BoolRoundTrip()
    {
        var ctx = NewContext();
        ctx.SetValue("done", true);

        Assert.Equal(true, ctx.GetValue("done"));
        Assert.IsType<bool>(ctx.GetValue("done"));
    }

    [Fact]
    public void GetValue_MissingKey_ReturnsNull()
    {
        var ctx = NewContext();

        Assert.Null(ctx.GetValue("absent"));
    }
}
