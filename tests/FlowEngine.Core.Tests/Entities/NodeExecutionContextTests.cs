using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests.Entities;

public class NodeExecutionContextTests
{
    private static NodeExecutionContext CreateContext(Dictionary<string, DataBatch>? inputs = null)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition { Id = Guid.NewGuid() },
            Inputs = inputs ?? new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
        };
    }

    // ===== GetInputBatch =====

    [Fact]
    public void GetInputBatch_PortExists_ReturnsBatch()
    {
        var batch = new DataBatch { Items = [new DataItem { Data = JsonValue.Create("test"), Success = true }] };
        var context = CreateContext(inputs: new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase) { ["input"] = batch });

        var result = context.GetInputBatch("input");

        Assert.Same(batch, result);
    }

    [Fact]
    public void GetInputBatch_PortMissing_ReturnsEmptyBatch()
    {
        var context = CreateContext();

        var result = context.GetInputBatch("nonexistent");

        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void GetInputBatch_DefaultPort_ReturnsInput()
    {
        var batch = new DataBatch { Items = [new DataItem { Data = JsonValue.Create("data"), Success = true }] };
        var context = CreateContext(inputs: new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase) { ["Input"] = batch });

        var result = context.GetInputBatch();

        Assert.Same(batch, result);
    }

    // ===== Ok(JsonNode?) =====

    [Fact]
    public void Ok_JsonNode_ReturnsSuccessResult()
    {
        var context = CreateContext();
        var data = new JsonObject { ["key"] = "value" };

        var result = context.Ok(data);

        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        var item = Assert.Single(result.Output.Items);
        Assert.Same(data, item.Data);
        Assert.True(item.Success);
    }

    [Fact]
    public void Ok_NullData_ReturnsSuccessWithNullItem()
    {
        var context = CreateContext();

        var result = context.Ok((JsonNode?)null);

        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        var item = Assert.Single(result.Output.Items);
        Assert.Null(item.Data);
    }

    // ===== Ok(DataBatch) =====

    [Fact]
    public void Ok_DataBatch_ReturnsSuccessWithSameBatch()
    {
        var context = CreateContext();
        var batch = new DataBatch { Items = [new DataItem { Data = JsonValue.Create("x"), Success = true }] };

        var result = context.Ok(batch);

        Assert.True(result.Success);
        Assert.Same(batch, result.Output);
    }

    [Fact]
    public void Ok_EmptyBatch_ReturnsSuccessWithEmptyBatch()
    {
        var context = CreateContext();
        var batch = new DataBatch();

        var result = context.Ok(batch);

        Assert.True(result.Success);
        Assert.Empty(result.Output.Items);
    }

    // ===== CatchToResult =====

    [Fact]
    public async Task CatchToResult_Success_ReturnsResult()
    {
        var context = CreateContext();

        var result = await context.CatchToResult(ct =>
        {
            return Task.FromResult(context.Ok(JsonValue.Create("ok")));
        }, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task CatchToResult_OperationCanceled_ReturnsCancelled()
    {
        var context = CreateContext();

        var result = await context.CatchToResult(ct =>
        {
            throw new OperationCanceledException();
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal("Cancelled", result.Error.Code);
    }

    [Fact]
    public async Task CatchToResult_ScriptError_ReturnsScriptError()
    {
        var context = CreateContext();

        var result = await context.CatchToResult(ct =>
        {
            throw new ScriptErrorException(new Script { Source = "test" }, "Script failed");
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal("ScriptError", result.Error.Code);
    }

    [Fact]
    public async Task CatchToResult_Timeout_ReturnsTimeout()
    {
        var context = CreateContext();

        var result = await context.CatchToResult(ct =>
        {
            throw new TimeoutException("Timed out");
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal("Timeout", result.Error.Code);
    }

    [Fact]
    public async Task CatchToResult_GenericException_ReturnsUnexpectedError()
    {
        var context = CreateContext();

        var result = await context.CatchToResult(ct =>
        {
            throw new InvalidOperationException("Something went wrong");
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal("UnexpectedError", result.Error.Code);
    }

    // ===== ToErrorResult =====

    [Fact]
    public void ToErrorResult_OperationCanceled_MapsCorrectly()
    {
        var context = CreateContext();

        var error = context.ToErrorResult(new OperationCanceledException());

        Assert.Equal("Cancelled", error.Code);
        Assert.Equal(context.Node.Id, error.NodeDefinitionId);
    }

    [Fact]
    public void ToErrorResult_ScriptError_MapsCorrectly()
    {
        var context = CreateContext();

        var error = context.ToErrorResult(new ScriptErrorException(new Script { Source = "test" }, "eval failed"));

        Assert.Equal("ScriptError", error.Code);
    }

    [Fact]
    public void ToErrorResult_GenericException_MapsToUnexpectedError()
    {
        var context = CreateContext();

        var error = context.ToErrorResult(new InvalidOperationException("bad"));

        Assert.Equal("UnexpectedError", error.Code);
    }

    // ===== GuardSsrf =====

    [Fact]
    public void GuardSsrf_NullUrl_ReturnsNull()
    {
        var context = CreateContext();

        var result = context.GuardSsrf(null);

        Assert.Null(result);
    }

    [Fact]
    public void GuardSsrf_EmptyUrl_ReturnsNull()
    {
        var context = CreateContext();

        var result = context.GuardSsrf("");

        Assert.Null(result);
    }

    [Fact]
    public void GuardSsrf_SafeUrl_ReturnsNull()
    {
        var context = CreateContext();

        // 1.1.1.1 is a public Cloudflare DNS IP, not an internal/loopback address.
        // Using an IP literal avoids DNS dependency in the test environment.
        var result = context.GuardSsrf("https://1.1.1.1/");

        Assert.Null(result);
    }

    [Fact]
    public void GuardSsrf_LoopbackUrl_ReturnsError()
    {
        var context = CreateContext();

        var result = context.GuardSsrf("http://127.0.0.1:8080/admin");

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal("SsrfBlocked", result.Error.Code);
    }

    // ===== TryParseJson (to JsonDocument) =====

    [Fact]
    public void TryParseJson_ValidJson_ReturnsTrue()
    {
        var context = CreateContext();

        var success = context.TryParseJson("""{"key": "value"}""", out var doc, out var errorCode);

        Assert.True(success);
        Assert.Null(errorCode);
        Assert.NotNull(doc);
        doc.Dispose();
    }

    [Fact]
    public void TryParseJson_InvalidJson_ReturnsFalse()
    {
        var context = CreateContext();

        var success = context.TryParseJson("not json", out var doc, out var errorCode);

        Assert.False(success);
        Assert.Equal("InvalidJson", errorCode);
    }

    [Fact]
    public void TryParseJson_NullString_ThrowsArgumentNullException()
    {
        var context = CreateContext();

        Assert.Throws<ArgumentNullException>(() => context.TryParseJson(null!, out var doc, out var errorCode));
    }

    // ===== TryParseJson<T> =====

    [Fact]
    public void TryParseJsonT_ValidJson_ReturnsTrue()
    {
        var context = CreateContext();
        var json = """{"name": "test", "value": 42}""";

        var success = context.TryParseJson(json, out Dictionary<string, JsonElement>? result, out var errorCode);

        Assert.True(success);
        Assert.Null(errorCode);
        Assert.NotNull(result);
        Assert.Equal("test", result!["name"].GetString());
        Assert.Equal(42, result["value"].GetInt32());
    }

    [Fact]
    public void TryParseJsonT_InvalidJson_ReturnsFalse()
    {
        var context = CreateContext();

        var success = context.TryParseJson<JsonObject>("not json", out var result, out var errorCode);

        Assert.False(success);
        Assert.Equal("InvalidJson", errorCode);
        Assert.Null(result);
    }

    [Fact]
    public void TryParseJsonT_NullJson_ReturnsFalse()
    {
        var context = CreateContext();

        // "null" is valid JSON but deserializes as a null reference,
        // so TryParseJson<T> returns false with InvalidJson.
        var success = context.TryParseJson<JsonObject>("null", out var result, out var errorCode);

        Assert.False(success);
        Assert.Equal("InvalidJson", errorCode);
    }
}
