using FlowEngine.Core.Scripting;
using Jint;

namespace FlowEngine.Core.Tests.Scripting;

public sealed class ScriptCompilerTests
{
    [Fact]
    public void Compile_EmptySource_ReturnsUndefined()
    {
        var script = new Script { Source = "" };
        var prepared = ScriptCompiler.Compile(script);

        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
        Assert.Equal(string.Empty, script.Source);

        using var engine = JsEngine.Create();
        var result = engine.EvaluatePrepared(prepared);

        Assert.True(result.IsUndefined());
    }

    [Fact]
    public void Compile_SingleExpression_WrapsWithReturn()
    {
        var script = new Script { Source = "1 + 2" };
        var prepared = ScriptCompiler.Compile(script);

        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
        Assert.Equal("1 + 2", script.Source);

        using var engine = JsEngine.Create();
        var result = engine.EvaluatePrepared(prepared);

        Assert.Equal(3, result.AsNumber());
    }

    [Fact]
    public void Compile_MultipleStatementsWithoutReturn_WrapsWithIifeAndUndefined()
    {
        var script = new Script { Source = "const x = 1; const y = 2;" };
        var prepared = ScriptCompiler.Compile(script);

        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
        Assert.Equal("const x = 1; const y = 2;", script.Source);

        using var engine = JsEngine.Create();
        var result = engine.EvaluatePrepared(prepared);

        Assert.True(result.IsUndefined());
    }

    [Fact]
    public void Compile_TopLevelReturn_WrapsWithIife()
    {
        var script = new Script { Source = "const x = 5; return x * 2;" };
        var prepared = ScriptCompiler.Compile(script);

        Assert.True(prepared.IsValid);
        Assert.NotNull(prepared.Program);

        using var engine = JsEngine.Create();
        var result = engine.EvaluatePrepared(prepared);

        Assert.Equal(10, result.AsNumber());
    }

    [Fact]
    public void Compile_ReturnInsideFunction_DoesNotTreatAsTopLevelReturn()
    {
        var script = new Script { Source = "function f() { return 7; } return f();" };
        var prepared = ScriptCompiler.Compile(script);

        Assert.True(prepared.IsValid);
        Assert.NotNull(prepared.Program);

        using var engine = JsEngine.Create();
        var result = engine.EvaluatePrepared(prepared);

        Assert.Equal(7, result.AsNumber());
    }

    [Fact]
    public void Compile_MultipleStatementsWithoutReturn_EndingWithExpression_ReturnsUndefined()
    {
        var script = new Script { Source = "const x = 1; 2 + 3;" };
        var prepared = ScriptCompiler.Compile(script);

        Assert.True(prepared.IsValid);
        Assert.NotNull(prepared.Program);

        using var engine = JsEngine.Create();
        var result = engine.EvaluatePrepared(prepared);

        Assert.True(result.IsUndefined());
    }

    [Fact]
    public void Compile_SingleExpression_WithTrailingSemicolon_ReturnsValue()
    {
        var script = new Script { Source = "1 + 1;" };
        var prepared = ScriptCompiler.Compile(script);

        Assert.True(prepared.IsValid);
        Assert.NotNull(prepared.Program);

        using var engine = JsEngine.Create();
        var result = engine.EvaluatePrepared(prepared);

        Assert.Equal(2, result.AsNumber());
    }

    [Fact]
    public void Compile_NonJavaScript_ThrowsNotSupportedException()
    {
        var script = new Script { Source = "print('hi')", Language = ScriptLanguage.Python };

        var ex = Assert.Throws<NotSupportedException>(() => ScriptCompiler.Compile(script));
        Assert.Contains("Python", ex.Message);
    }
}
