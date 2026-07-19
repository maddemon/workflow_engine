using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests;

public class ScriptErrorExceptionTests
{
    [Fact]
    public void Constructor_SetsScriptAndReason()
    {
        var script = new Script { Source = "x = 1" };
        var ex = new ScriptErrorException(script, "syntax error");

        Assert.Same(script, ex.Script);
        Assert.Equal("syntax error", ex.Reason);
        Assert.Contains("x = 1", ex.Message);
    }

    [Fact]
    public void Constructor_WithInnerException_SetsInnerException()
    {
        var script = new Script { Source = "x" };
        var inner = new InvalidOperationException("boom");

        var ex = new ScriptErrorException(script, "failed", inner);

        Assert.Same(inner, ex.InnerException);
    }
}
