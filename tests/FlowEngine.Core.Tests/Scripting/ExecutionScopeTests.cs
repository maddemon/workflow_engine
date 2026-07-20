using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;
using Jint;
using Jint.Native;

namespace FlowEngine.Core.Tests.Scripting;

/// <summary>
/// <see cref="ExecutionScope"/> 行为测试：验证全局变量与逐项变量被正确注入到 JsEngine，
/// 且空 key 的全局变量被跳过、逐项变量覆盖式写入。
/// </summary>
public class ExecutionScopeTests
{
    private static NodeExecutionContext BuildContext()
        => new()
        {
            ExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Node = new NodeDefinition { Name = "myNode", TypeName = "myType" },
            Workflow = new Workflow { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") },
            RawParameters = new Dictionary<string, object> { ["p1"] = "v1" },
            GlobalVariables = new Dictionary<string, object?>
            {
                ["gVar"] = 42,
                [""] = "should-be-skipped",
            },
        };

    [Fact]
    public void ApplyGlobalVariables_InjectsNonEmptyKeys_AndSkipsEmptyKey()
    {
        using var engine = JsEngine.Create();
        var context = BuildContext();

        engine.ApplyGlobalVariables(context);

        // 非空 key 注入成功。
        Assert.Equal(42, engine.Evaluate("gVar").AsNumber());
        // 空 key 被跳过（Evaluate 空名表达式不应取到该值，且不抛异常）。
        Assert.True(engine.Evaluate("typeof gVar").AsString() == "number");
    }

    [Fact]
    public void ApplyItemScope_InjectsItemVariables()
    {
        using var engine = JsEngine.Create();
        var context = BuildContext();
        var current = JsonNode.Parse("""{"x":5}""");
        var all = new List<object?> { current };

        engine.ApplyItemScope(context, current, all, 3);

        Assert.Equal(3, engine.Evaluate("$itemIndex").AsNumber());
        Assert.Equal(3, engine.Evaluate("$runIndex").AsNumber());
        // $json 指向当前 item。
        Assert.Equal(5, engine.Evaluate("$json.x").AsNumber());
        // $input 为 InputContainer 且计数正确。
        var container = engine.Evaluate("$input").ToObject() as InputContainer;
        Assert.NotNull(container);
        Assert.Equal(1, container!.count());
    }

    [Fact]
    public void ApplyNodeScope_InjectsBothGlobalAndItemVariables()
    {
        using var engine = JsEngine.Create();
        var context = BuildContext();
        var current = JsonNode.Parse("""{"y":9}""");
        var all = new List<object?> { current };

        engine.ApplyNodeScope(context, current, all, 0);

        // 全局变量。
        Assert.Equal(42, engine.Evaluate("gVar").AsNumber());
        // 逐项变量。
        Assert.Equal(9, engine.Evaluate("$json.y").AsNumber());
        Assert.Equal(0, engine.Evaluate("$itemIndex").AsNumber());
        var container = engine.Evaluate("$input").ToObject() as InputContainer;
        Assert.NotNull(container);
        Assert.Equal(1, container!.count());
    }
}
