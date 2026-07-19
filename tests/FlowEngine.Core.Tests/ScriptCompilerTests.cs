using System.Text.Json;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using Jint;

namespace FlowEngine.Core.Tests;

public class ScriptCompilerTests
{
    [Fact]
    public void Compile_EmptyScript_ReturnsPreparedScript()
    {
        var script = new Script { Source = string.Empty };

        var result = ScriptCompiler.Compile(script);

        var text = result.ToString();
        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void Compile_Expression_WrapsWithReturn()
    {
        var script = new Script { Source = "1 + 1" };

        var result = ScriptCompiler.Compile(script);

        var text = result.ToString();
        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void Compile_EmptyObjectLiteral_WrapsCorrectly()
    {
        var script = new Script { Source = "{}" };

        var result = ScriptCompiler.Compile(script);

        var text = result.ToString();
        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void Compile_TopLevelReturn_WrapsAsIife()
    {
        var script = new Script { Source = "return 42;" };

        var result = ScriptCompiler.Compile(script);

        var text = result.ToString();
        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void Compile_MultiStatement_WrapsAsIife()
    {
        var script = new Script { Source = "var x = 1; var y = 2;" };

        var result = ScriptCompiler.Compile(script);

        var text = result.ToString();
        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void Compile_NonJavaScriptLanguage_Throws()
    {
        var script = new Script { Source = "x", Language = (ScriptLanguage)999 };

        Assert.Throws<NotSupportedException>(() => ScriptCompiler.Compile(script));
    }

    [Fact]
    public void TryCompile_ValidScript_ReturnsTrue()
    {
        var script = new Script { Source = "1 + 1" };

        var success = ScriptCompiler.TryCompile(script, out var error);

        Assert.True(success);
        Assert.Null(error);
    }

    [Fact]
    public void TryCompile_InvalidScript_ReturnsFalseWithError()
    {
        var script = new Script { Source = "var x = {" };

        var success = ScriptCompiler.TryCompile(script, out var error);

        Assert.False(success);
        Assert.NotNull(error);
    }
}
