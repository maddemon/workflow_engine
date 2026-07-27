using FlowEngine.Core.Scripting;
using Jint;
using Jint.Native;

namespace FlowEngine.Core.Tests;

/// <summary>
/// SEC-7 纵深防御：验证 <see cref="JsEngine"/> 沙箱不再把 <c>Function</c> 暴露为全局，
/// 从而工作流脚本无法用 <c>new Function(...)</c> / <c>Function(...)</c> 从字符串构造可执行函数。
/// </summary>
public class JsEngineSandboxTests
{
    [Fact]
    public void Sandbox_FunctionGlobalRemoved_ThrowsOnReference()
    {
        using var engine = JsEngine.Create();

        // 移除白名单后，全局对象上不再存在 Function 自有属性，
        // 直接引用会抛出 ReferenceError（而不是返回可调用构造器）。
        bool threw = false;
        try
        {
            engine.Evaluate("Function");
        }
        catch (Exception)
        {
            threw = true;
        }

        Assert.True(threw, "沙箱应移除 Function 全局，引用它须抛出 ReferenceError");
    }

    [Fact]
    public void Sandbox_FunctionConstructorCall_Throws()
    {
        using var engine = JsEngine.Create();

        // 即使尝试以函数/构造器方式调用，也必须失败（不允许从字符串构造可执行代码）。
        bool threw = false;
        try
        {
            engine.Evaluate("Function('return 1')()");
        }
        catch (Exception)
        {
            threw = true;
        }

        Assert.True(threw, "Function('return 1')() 应被沙箱阻止");

        threw = false;
        try
        {
            engine.Evaluate("new Function('return 1')()");
        }
        catch (Exception)
        {
            threw = true;
        }

        Assert.True(threw, "new Function('return 1')() 应被沙箱阻止");
    }

    [Fact]
    public void Sandbox_SafeScript_StillWorks()
    {
        using var engine = JsEngine.Create();

        var sum = engine.Evaluate("1 + 2");
        Assert.Equal(3, sum.AsNumber());

        var json = engine.Evaluate("JSON.stringify({a:1})");
        Assert.Equal("{\"a\":1}", json.AsString());
    }
}
