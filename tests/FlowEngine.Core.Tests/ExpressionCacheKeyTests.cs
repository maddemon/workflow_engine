using FlowEngine.Core.Expressions;

namespace FlowEngine.Core.Tests;

public class ExpressionCacheKeyTests
{
    [Fact]
    public void ExpressionCacheKey_Properties_RoundTrip()
    {
        var key = new ExpressionCacheKey("a + b", "hash1", "hash2");

        Assert.Equal("a + b", key.Expression);
        Assert.Equal("hash1", key.InputSchemaHash);
        Assert.Equal("hash2", key.ParameterSchemaHash);
    }

    [Fact]
    public void ExpressionCacheKey_EqualValues_AreEqual()
    {
        var a = new ExpressionCacheKey("x", "h1", "h2");
        var b = new ExpressionCacheKey("x", "h1", "h2");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void ExpressionCacheKey_DifferentValues_AreNotEqual()
    {
        var a = new ExpressionCacheKey("x", "h1", "h2");
        var b = new ExpressionCacheKey("y", "h1", "h2");

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }
}
