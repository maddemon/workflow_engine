using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// <see cref="CodeParameterExtractor.Extract"/> 测试。验证其从原始参数字典中剥离
/// type != Script 且 hint 为 CodeEditor / Script 的参数，其余参数保持不变。
/// </summary>
public class CodeParameterExtractorTests
{
    [Fact]
    public void Extract_RemovesCodeEditorParams_KeepsScriptAndNormal()
    {
        var descriptor = new NodeTypeDescriptor
        {
            TypeName = "test",
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "code", Type = ParameterType.String, Hint = PresentationHint.CodeEditor },
                new() { Name = "scriptExpr", Type = ParameterType.Script, Hint = PresentationHint.Script },
                new() { Name = "normal", Type = ParameterType.String, Hint = PresentationHint.Default },
            }
        };

        var raw = new Dictionary<string, object>
        {
            ["code"] = "function(){}",
            ["scriptExpr"] = "$json.x",
            ["normal"] = "value",
        };

        var extracted = CodeParameterExtractor.Extract(raw, descriptor);

        Assert.Single(extracted);
        Assert.True(extracted.ContainsKey("code"));
        Assert.Equal("function(){}", extracted["code"]);
        // 原始字典中 code 被移除，script/normal 保留。
        Assert.False(raw.ContainsKey("code"));
        Assert.True(raw.ContainsKey("scriptExpr"));
        Assert.True(raw.ContainsKey("normal"));
    }

    [Fact]
    public void Extract_MatchesCaseInsensitively()
    {
        var descriptor = new NodeTypeDescriptor
        {
            TypeName = "test",
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "MyCode", Type = ParameterType.Code, Hint = PresentationHint.CodeEditor },
            }
        };

        var raw = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["mycode"] = "SELECT 1" };

        var extracted = CodeParameterExtractor.Extract(raw, descriptor);

        Assert.True(extracted.ContainsKey("mycode"));
        Assert.Equal("SELECT 1", extracted["mycode"]);
        Assert.False(raw.ContainsKey("mycode"));
    }

    [Fact]
    public void Extract_NoCodeParams_ReturnsEmptyAndLeavesRaw()
    {
        var descriptor = new NodeTypeDescriptor
        {
            TypeName = "test",
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "a", Type = ParameterType.String, Hint = PresentationHint.Default },
                new() { Name = "b", Type = ParameterType.Script, Hint = PresentationHint.Script },
            }
        };

        var raw = new Dictionary<string, object> { ["a"] = "1", ["b"] = "$json.x" };

        var extracted = CodeParameterExtractor.Extract(raw, descriptor);

        Assert.Empty(extracted);
        Assert.True(raw.ContainsKey("a"));
        Assert.True(raw.ContainsKey("b"));
    }
}
