using FlowEngine.Runtime.Expressions.Exceptions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Expressions.Exceptions;

/// <summary>
/// 表达式异常类型构造与属性覆盖测试。
/// </summary>
public class ExpressionExceptionTests
{
    [Fact]
    public void NodeOutputNotFoundException_CarriesNodeName()
    {
        var ex = new NodeOutputNotFoundException("$node.out", "myNode");
        Assert.Equal("myNode", ex.NodeName);
        Assert.Equal("$node.out", ex.Expression);
        Assert.Contains("myNode", ex.Message);
    }

    [Fact]
    public void FieldNotFoundException_CarriesFieldName_AndAvailableFields()
    {
        var ex = new FieldNotFoundException("$json.x", "x", new[] { "a", "b" });
        Assert.Equal("x", ex.FieldName);
        Assert.Equal(2, ex.AvailableFields.Count);
    }

    [Fact]
    public void ExpressionEvaluationException_BaseProperties_WithNullAvailableFields()
    {
        var ex = new FieldNotFoundException("expr", "f", null);
        Assert.Equal("expr", ex.Expression);
        Assert.Empty(ex.AvailableFields);
        Assert.False(string.IsNullOrEmpty(ex.Reason));
    }
}
