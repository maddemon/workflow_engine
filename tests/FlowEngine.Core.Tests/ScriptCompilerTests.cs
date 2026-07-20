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

        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
        Assert.Equal(string.Empty, script.Source);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Program);
    }

    [Fact]
    public void Compile_Expression_WrapsWithReturn()
    {
        var script = new Script { Source = "1 + 1" };

        var result = ScriptCompiler.Compile(script);

        Assert.Equal("1 + 1", script.Source);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Program);
    }

    [Fact]
    public void Compile_EmptyObjectLiteral_WrapsCorrectly()
    {
        var script = new Script { Source = "{}" };

        var result = ScriptCompiler.Compile(script);

        Assert.Equal("{}", script.Source);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Program);
    }

    [Fact]
    public void Compile_TopLevelReturn_WrapsAsIife()
    {
        var script = new Script { Source = "return 42;" };

        var result = ScriptCompiler.Compile(script);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Program);
    }

    [Fact]
    public void Compile_MultiStatement_WrapsAsIife()
    {
        var script = new Script { Source = "var x = 1; var y = 2;" };

        var result = ScriptCompiler.Compile(script);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Program);
    }

    [Fact]
    public void Compile_NonJavaScriptLanguage_Throws()
    {
        var script = new Script { Source = "x", Language = (ScriptLanguage)999 };

        var ex = Assert.Throws<NotSupportedException>(() => ScriptCompiler.Compile(script));
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void TryCompile_ValidScript_ReturnsTrue()
    {
        var script = new Script { Source = "1 + 1" };

        var success = ScriptCompiler.TryCompile(script, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
    }

    [Fact]
    public void TryCompile_InvalidScript_ReturnsFalseWithError()
    {
        var script = new Script { Source = "var x = {" };

        var success = ScriptCompiler.TryCompile(script, out var error);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.NotNull(error!.Message);
        Assert.Same(script, error.Script);
    }
}
