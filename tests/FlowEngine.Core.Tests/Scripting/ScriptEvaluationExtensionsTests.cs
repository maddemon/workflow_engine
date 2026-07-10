using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Core.Tests.Scripting;

public sealed class ScriptEvaluationExtensionsTests
{
    private static NodeExecutionContext CreateContext(JsonNode? inputItem = null)
    {
        var inputs = new Dictionary<string, DataBatch>();
        if (inputItem is not null)
        {
            inputs[FlowConstants.PortNames.Input] = new DataBatch
            {
                Items =
                [
                    new DataItem { Data = inputItem, Success = true, SourceIndex = 0 }
                ]
            };
        }

        return new NodeExecutionContext
        {
            Workflow = new Workflow(),
            Node = new NodeDefinition(),
            ScriptCache = new ScriptCache(Options.Create(new JsEngineOptions())),
            Inputs = inputs,
            RawParameters = new Dictionary<string, object>(),
        };
    }

    [Fact]
    public async Task EvaluateAsync_Int_ReturnsValue()
    {
        var script = new Script { Source = "1 + 2" };
        var context = CreateContext();

        var result = await script.EvaluateAsync<int>(context, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task EvaluateAsync_Object_ReturnsJsonNode()
    {
        var script = new Script { Source = "({ a: 1, b: 'two' })" };
        var context = CreateContext();

        var json = await script.EvaluateAsync<JsonNode>(context, cancellationToken: TestContext.Current.CancellationToken);

        var obj = Assert.IsType<JsonObject>(json);
        Assert.Equal(1, obj["a"]!.GetValue<int>());
    }

    [Fact]
    public async Task EvaluateAsync_EmptyScript_ReturnsDefault()
    {
        var script = new Script();
        var context = CreateContext();

        var result = await script.EvaluateAsync<int>(context, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task EvaluateAsync_CompileError_Throws()
    {
        var script = new Script { Source = "x()" };
        var context = CreateContext();

        await Assert.ThrowsAsync<ScriptErrorException>(() => script.EvaluateAsync<int>(context, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_CompileError_ReturnsFailedResult()
    {
        var script = new Script { Source = "x()" };
        var context = CreateContext();

        var result = await script.ExecuteAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task EvaluateAsync_ResolvedValue_ShortCircuits()
    {
        var script = new Script { Source = "/* never executed */" }.WithResolvedValue(JsonValue.Create(42));
        var context = CreateContext();

        var result = await script.EvaluateAsync<int>(context, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvedValue_ReturnsSuccessResult()
    {
        var script = new Script { Source = "/* never executed */" }.WithResolvedValue(JsonValue.Create(true));
        var context = CreateContext();

        var result = await script.ExecuteAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.ToBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_ForItem_InjectsJsonVariable()
    {
        var item = JsonNode.Parse("{\"a\": 10}")!;
        var script = new Script { Source = "$json.a + 1" };
        var context = CreateContext(item);

        var result = await script.EvaluateAsync<int>(context, item, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(11, result);
    }

    [Fact]
    public async Task EvaluateAsync_WithGlobals_InjectsExtraGlobals()
    {
        var script = new Script { Source = "base * 2" };
        var context = CreateContext();

        var result = await script.EvaluateAsync<int>(context, TestContext.Current.CancellationToken, ("base", 5));

        Assert.Equal(10, result);
    }

    [Fact]
    public async Task EvaluateAsync_TypedConversions()
    {
        var context = CreateContext();

        Assert.True(await new Script { Source = "1 > 0" }.EvaluateAsync<bool>(context, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("hi", await new Script { Source = "'hi'" }.EvaluateAsync<string>(context, cancellationToken: TestContext.Current.CancellationToken));
        Assert.IsType<JsonArray>(await new Script { Source = "[1, 2]" }.EvaluateAsync<object>(context, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EvaluateAsync_ReusesManagedEngine()
    {
        var script = new Script { Source = "1" };
        var context = CreateContext();

        await script.EvaluateAsync<int>(context, cancellationToken: TestContext.Current.CancellationToken);
        var engine1 = context.GetOrCreateEngine();
        await script.EvaluateAsync<int>(context, cancellationToken: TestContext.Current.CancellationToken);
        var engine2 = context.GetOrCreateEngine();

        Assert.Same(engine1, engine2);
    }
}
