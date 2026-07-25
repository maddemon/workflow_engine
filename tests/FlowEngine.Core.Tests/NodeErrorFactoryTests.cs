using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Tests;

/// <summary>
/// EX-2：验证 <see cref="NodeErrorFactory"/> 构造的错误不向客户端泄露原始异常文本或堆栈。
/// </summary>
public class NodeErrorFactoryTests
{
    [Fact]
    public void Sanitize_WithoutRawMessage_DoesNotLeakStackTraceOrExceptionText()
    {
        var ex = new InvalidOperationException("secret table 'users' not found at /var/data/db");

        var error = NodeErrorFactory.Sanitize(ex, "NodeExecutionFailed", "node-1");

        Assert.Equal("NodeExecutionFailed", error.Code);
        Assert.Equal("node-1", error.NodeDefinitionId);
        Assert.Null(error.StackTrace);
        Assert.NotEqual(ex.Message, error.Message);
        Assert.DoesNotContain("users", error.Message);
        Assert.DoesNotContain("var/data", error.Message);
        Assert.Equal(NodeErrorFactory.SafeMessage, error.Message);
    }

    [Fact]
    public void Sanitize_WithSafeMessage_UsesProvidedMessageWithoutRawExceptionText()
    {
        var ex = new Exception("RAWCreds=supersecret; server=10.0.0.1");

        var error = NodeErrorFactory.Sanitize(ex, "LlmError", "agent-9", "LLM 调用失败，请稍后重试。");

        Assert.Equal("LlmError", error.Code);
        Assert.Null(error.StackTrace);
        Assert.Equal("LLM 调用失败，请稍后重试。", error.Message);
        Assert.DoesNotContain("supersecret", error.Message);
        Assert.DoesNotContain("10.0.0.1", error.Message);
    }

    [Fact]
    public void ToClientSafe_Null_ReturnsSafeDefault()
    {
        var safe = NodeErrorFactory.ToClientSafe(null);

        Assert.Equal("NodeExecutionFailed", safe.Code);
        Assert.Equal(NodeErrorFactory.SafeMessage, safe.Message);
        Assert.Null(safe.StackTrace);
        Assert.Empty(safe.Details);
    }

    [Fact]
    public void ToClientSafe_StripsRawMessage_KeepsCodeAndNode()
    {
        // EX-2 回归：含敏感原始文本的 NodeError 经 ToClientSafe 后，客户端仅看到通用安全描述，
        // 但 Code 与 NodeDefinitionId（前端定位所需）被保留。
        var raw = new NodeError
        {
            Code = "DbError",
            Message = "table 'secret_users' not found at /opt/data/prod",
            NodeDefinitionId = "dbRead-7",
            Details = new() { ["sqlState"] = "42P01" },
            StackTrace = "at FlowEngine.Plugins.Standard.DbReadNode.ExecuteAsync(...)",
        };

        var safe = NodeErrorFactory.ToClientSafe(raw);

        Assert.Equal("DbError", safe.Code);
        Assert.Equal("dbRead-7", safe.NodeDefinitionId);
        Assert.Equal(NodeErrorFactory.SafeMessage, safe.Message);
        Assert.Null(safe.StackTrace);
        Assert.Empty(safe.Details);
        Assert.DoesNotContain("secret_users", safe.Message);
        Assert.DoesNotContain("/opt/data", safe.Message);
    }
}
