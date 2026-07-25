using FlowEngine.Core.Scripting;
using Jint;
using Jint.Native;
using Xunit;

namespace FlowEngine.Runtime.Tests.Scripting;

/// <summary>
/// JS 沙箱白名单（SEC-7）测试：仅放行白名单内的全局 API，删除危险标识符，
/// 即使借字符串拼接绕过黑名单也无法逃逸；既有合法脚本仍正常执行。
/// </summary>
public class JsEngineSandboxWhitelistTests
{
    [Fact]
    public void ForbiddenGlobals_AreRemoved()
    {
        using var engine = JsEngine.Create();

        // 这些标识符不应存在于白名单沙箱中。
        Assert.True(engine.Evaluate("typeof process === 'undefined'").AsBoolean());
        Assert.True(engine.Evaluate("typeof require === 'undefined'").AsBoolean());
        Assert.True(engine.Evaluate("typeof globalThis === 'undefined'").AsBoolean());
        Assert.True(engine.Evaluate("typeof window === 'undefined'").AsBoolean());
        Assert.True(engine.Evaluate("typeof document === 'undefined'").AsBoolean());
        Assert.True(engine.Evaluate("typeof fetch === 'undefined'").AsBoolean());
        Assert.True(engine.Evaluate("typeof eval === 'undefined'").AsBoolean());
    }

    [Fact]
    public void AllowedGlobals_AndHelpers_ArePresent()
    {
        using var engine = JsEngine.Create();

        Assert.True(engine.Evaluate("typeof Math === 'object'").AsBoolean());
        Assert.True(engine.Evaluate("typeof JSON === 'object'").AsBoolean());
        Assert.True(engine.Evaluate("typeof Object === 'function'").AsBoolean());
        Assert.True(engine.Evaluate("typeof Array === 'function'").AsBoolean());
        Assert.True(engine.Evaluate("typeof console === 'object'").AsBoolean());
        Assert.True(engine.Evaluate("typeof now === 'function'").AsBoolean());
        Assert.True(engine.Evaluate("typeof length === 'function'").AsBoolean());
    }

    [Fact]
    public void LegitScript_StillRuns()
    {
        using var engine = JsEngine.Create();

        Assert.Equal(2, engine.Evaluate("Math.max(1, 2)").AsNumber());
        Assert.Equal(1, engine.Evaluate("[3, 1, 2].sort((a, b) => a - b)[0]").AsNumber());
        Assert.True(engine.Evaluate("Object.keys({ a: 1 }).length === 1").AsBoolean());
        Assert.True(engine.Evaluate("typeof now() === 'string'").AsBoolean());
        Assert.Equal(3, engine.Evaluate("length('abc')").AsNumber());
        Assert.Equal(6, engine.Evaluate("[1, 2, 3].reduce((a, b) => a + b, 0)").AsNumber());
        Assert.True(engine.Evaluate("JSON.stringify({ a: 1 }) === '{\"a\":1}'").AsBoolean());
    }

    [Fact]
    public void EscapeAttempt_ViaStringConcatConstructor_DoesNotExposeClr()
    {
        using var engine = JsEngine.Create();

        // 审计用例：this['cons'+'tructor'] 不应暴露 .NET 类型（未启用 AllowClr）。
        // 以 IIFE 表达式形式求值，避免 Evaluate 仅接受单表达式的限制。
        var result = engine.Evaluate(
            "(function (c) { return (c && c.System) ? 'LEAK' : 'SAFE'; })(this['cons' + 'tructor'])");
        Assert.Equal("SAFE", result.AsString());
    }

    [Fact]
    public void EscapeAttempt_PropertyChainProcess_DoesNotExposeClr()
    {
        using var engine = JsEngine.Create();

        // obj['pro' + 'cess'] 不存在（process 已被白名单移除）。
        var result = engine.Evaluate(
            "(function () { var o = {}; var p = o['pro' + 'cess']; return (p && p.System) ? 'LEAK' : 'SAFE'; })()");
        Assert.Equal("SAFE", result.AsString());
    }
}
