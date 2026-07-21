using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Options;

namespace FlowEngine.Core.Tests.Scripting;

public sealed class ScriptParameterPreEvaluatorCoreTests
{
    [Fact]
    public async Task PreEvaluateAsync_ExpressionScript_ResolvesAndWritesResolvedValue()
    {
        var descriptor = new NodeTypeDescriptor
        {
            TypeName = "x",
            Parameters = [new ParameterDefinition { Name = "expr", Type = ParameterType.Script, Hint = PresentationHint.Expression }]
        };
        var rawParameters = new Dictionary<string, object> { ["expr"] = (Script)"1 + 2" };

        var context = new NodeExecutionContext
        {
            Workflow = new Workflow(),
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = rawParameters,
            ResolvedParameters = rawParameters
        };
        var scriptContext = new ScriptContext(context);

        using var js = JsEngine.Create();
        var scriptCache = new ScriptCache(Options.Create(new JsEngineOptions()));

        await ScriptParameterPreEvaluatorCore.PreEvaluateAsync(rawParameters, descriptor, scriptContext, js, scriptCache, CancellationToken.None);

        var resolved = Assert.IsType<Script>(rawParameters["expr"]);
        Assert.NotNull(resolved.ResolvedValue);
        Assert.Equal(3, resolved.ResolvedValue!.GetValue<int>());
    }

    [Fact]
    public async Task PreEvaluateAsync_NonExpressionScript_StaysUnresolved()
    {
        var descriptor = new NodeTypeDescriptor
        {
            TypeName = "x",
            Parameters = [new ParameterDefinition { Name = "code", Type = ParameterType.Script }]
        };
        var rawParameters = new Dictionary<string, object> { ["code"] = (Script)"return 1 + 2;" };

        var context = new NodeExecutionContext
        {
            Workflow = new Workflow(),
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = rawParameters,
            ResolvedParameters = rawParameters
        };
        var scriptContext = new ScriptContext(context);

        using var js = JsEngine.Create();
        var scriptCache = new ScriptCache(Options.Create(new JsEngineOptions()));

        await ScriptParameterPreEvaluatorCore.PreEvaluateAsync(rawParameters, descriptor, scriptContext, js, scriptCache, CancellationToken.None);

        var resolved = Assert.IsType<Script>(rawParameters["code"]);
        Assert.Null(resolved.ResolvedValue);
    }
}
