using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests;

public class NodeExecutionContextTests
{
    [Fact]
    public void NodeExecutionContext_GetParameter_FromResolvedParameters_ReturnsValue()
    {
        var ctx = new NodeExecutionContext
        {
            ResolvedParameters = new Dictionary<string, object> { ["key"] = "value" }
        };

        var result = ctx.GetParameter<string>("key");

        Assert.Equal("value", result);
    }

    [Fact]
    public void NodeExecutionContext_GetParameter_FromRawParameters_ReturnsValue()
    {
        var ctx = new NodeExecutionContext
        {
            ResolvedParameters = new Dictionary<string, object>(),
            RawParameters = new Dictionary<string, object> { ["key"] = "raw" }
        };

        var result = ctx.GetParameter<string>("key");

        Assert.Equal("raw", result);
    }

    [Fact]
    public void NodeExecutionContext_GetParameter_NotFound_ReturnsNull()
    {
        var ctx = new NodeExecutionContext();

        var result = ctx.GetParameter<string>("missing");

        Assert.Null(result);
    }

    [Fact]
    public void NodeExecutionContext_ErrorResult_SetsNodeDefinitionId()
    {
        var ctx = new NodeExecutionContext { Node = new NodeDefinition { Id = "node-1" } };

        var result = ctx.ErrorResult("E1", "msg");

        Assert.False(result.Success);
        Assert.Equal("E1", result.Error?.Code);
        Assert.Equal("node-1", result.Error?.NodeDefinitionId);
    }

    [Fact]
    public void NodeExecutionContext_InputData_WithInput_ReturnsDeserializedObject()
    {
        var ctx = new NodeExecutionContext
        {
            Inputs = new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch
                {
                    Items = [new DataItem { Data = JsonNode.Parse("{\"a\":1}") }]
                }
            }
        };

        var result = ctx.InputData;

        Assert.NotNull(result);
    }

    [Fact]
    public void NodeExecutionContext_InputData_NoInput_ReturnsNull()
    {
        var ctx = new NodeExecutionContext();

        var result = ctx.InputData;

        Assert.Null(result);
    }

    [Fact]
    public void NodeExecutionContext_GetInputPayload_WithInput_ReturnsData()
    {
        var node = JsonNode.Parse("{\"a\":1}");
        var ctx = new NodeExecutionContext
        {
            Inputs = new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch
                {
                    Items = [new DataItem { Data = node }]
                }
            }
        };

        var result = ctx.GetInputPayload();

        Assert.Same(node, result);
    }

    [Fact]
    public async Task NodeExecutionContext_ResolveCredentialAsync_ById_ReturnsCredential()
    {
        var credentialId = Guid.NewGuid();
        var credential = new CredentialValue { Name = "c" };
        var ctx = new NodeExecutionContext
        {
            Credentials = new FakeCredentialAccessor { ById = credential },
            Logger = new FakeNodeExecutionLogger()
        };

        var result = await ctx.ResolveCredentialAsync(credentialId.ToString(), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("c", result!.Name);
    }

    [Fact]
    public async Task NodeExecutionContext_ResolveCredentialAsync_ByName_ReturnsCredential()
    {
        var credential = new CredentialValue { Name = "my-cred" };
        var ctx = new NodeExecutionContext
        {
            Credentials = new FakeCredentialAccessor { ByName = credential },
            Logger = new FakeNodeExecutionLogger()
        };

        var result = await ctx.ResolveCredentialAsync("my-cred", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("my-cred", result!.Name);
    }

    [Fact]
    public async Task NodeExecutionContext_ResolveCredentialAsync_Empty_ReturnsNull()
    {
        var ctx = new NodeExecutionContext();

        var result = await ctx.ResolveCredentialAsync(string.Empty, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task NodeExecutionContext_ResolveCredentialAsync_Exception_ReturnsNull()
    {
        var ctx = new NodeExecutionContext
        {
            Credentials = new FakeCredentialAccessor { Throw = new InvalidOperationException("fail") },
            Logger = new FakeNodeExecutionLogger()
        };

        var result = await ctx.ResolveCredentialAsync("name", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public void NodeExecutionContext_GuardSsrf_InternalUrl_ReturnsErrorResult()
    {
        var ctx = new NodeExecutionContext { Node = new NodeDefinition { Id = "n" } };

        var result = ctx.GuardSsrf("http://127.0.0.1/api");

        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public void NodeExecutionContext_GuardSsrf_ExternalUrl_ReturnsNull()
    {
        var ctx = new NodeExecutionContext();

        var result = ctx.GuardSsrf("http://example.com/api");

        Assert.Null(result);
    }

    [Fact]
    public void NodeExecutionContext_GuardSsrf_Empty_ReturnsNull()
    {
        var ctx = new NodeExecutionContext();

        var result = ctx.GuardSsrf(string.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void NodeExecutionContext_CreateSingleResult_ReturnsResultWithData()
    {
        var ctx = new NodeExecutionContext();
        var data = JsonValue.Create(42);

        var result = ctx.CreateSingleResult(data, false);

        Assert.False(result.Success);
        Assert.Single(result.Output.Items);
        Assert.Same(data, result.Output.Items[0].Data);
    }

    [Fact]
    public void NodeExecutionContext_GetInputBatch_ExistingPort_ReturnsBatch()
    {
        var batch = new DataBatch { Items = [new DataItem()] };
        var ctx = new NodeExecutionContext
        {
            Inputs = new Dictionary<string, DataBatch> { [FlowConstants.PortNames.Input] = batch }
        };

        var result = ctx.GetInputBatch();

        Assert.Same(batch, result);
    }

    [Fact]
    public void NodeExecutionContext_GetInputBatch_MissingPort_ReturnsEmptyBatch()
    {
        var ctx = new NodeExecutionContext();

        var result = ctx.GetInputBatch("missing");

        Assert.Empty(result.Items);
    }

    [Fact]
    public void NodeExecutionContext_Ok_WithNode_ReturnsSuccessResult()
    {
        var ctx = new NodeExecutionContext();
        var data = JsonValue.Create(42);

        var result = ctx.Ok(data);

        Assert.True(result.Success);
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public void NodeExecutionContext_Ok_WithBatch_ReturnsSuccessResult()
    {
        var ctx = new NodeExecutionContext();
        var batch = new DataBatch { Items = [new DataItem { Data = JsonValue.Create(1) }] };

        var result = ctx.Ok(batch);

        Assert.True(result.Success);
        Assert.Same(batch, result.Output);
    }

    [Fact]
    public async Task NodeExecutionContext_CatchToResult_Success_ReturnsResult()
    {
        var ctx = new NodeExecutionContext();
        var expected = new NodeExecutionResult { Success = true };

        var result = await ctx.CatchToResult(_ => Task.FromResult(expected), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task NodeExecutionContext_CatchToResult_Cancelled_ReturnsCancelledError()
    {
        var ctx = new NodeExecutionContext();

        var result = await ctx.CatchToResult(_ => throw new OperationCanceledException(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowConstants.ErrorCodes.Cancelled, result.Error?.Code);
    }

    [Fact]
    public async Task NodeExecutionContext_CatchToResult_ScriptError_ReturnsScriptError()
    {
        var ctx = new NodeExecutionContext();

        var result = await ctx.CatchToResult(_ => throw new ScriptErrorException(new Script { Source = "x" }, "err"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowConstants.ErrorCodes.ScriptError, result.Error?.Code);
    }

    [Fact]
    public async Task NodeExecutionContext_CatchToResult_Timeout_ReturnsTimeoutError()
    {
        var ctx = new NodeExecutionContext();

        var result = await ctx.CatchToResult(_ => throw new TimeoutException(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowConstants.ErrorCodes.Timeout, result.Error?.Code);
    }

    [Fact]
    public async Task NodeExecutionContext_CatchToResult_GenericException_ReturnsUnexpectedError()
    {
        var ctx = new NodeExecutionContext();

        var result = await ctx.CatchToResult(_ => throw new InvalidOperationException("boom"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowConstants.ErrorCodes.UnexpectedError, result.Error?.Code);
    }

    [Fact]
    public void NodeExecutionContext_ToErrorResult_OperationCanceled_ReturnsCancelledError()
    {
        var ctx = new NodeExecutionContext { Node = new NodeDefinition { Id = "n" } };

        var result = ctx.ToErrorResult(new OperationCanceledException());

        Assert.Equal(FlowConstants.ErrorCodes.Cancelled, result.Code);
        Assert.Equal("n", result.NodeDefinitionId);
    }

    [Fact]
    public void NodeExecutionContext_ToErrorResult_ScriptError_ReturnsScriptError()
    {
        var ctx = new NodeExecutionContext { Node = new NodeDefinition { Id = "n" } };

        var result = ctx.ToErrorResult(new ScriptErrorException(new Script { Source = "x" }, "err"));

        Assert.Equal(FlowConstants.ErrorCodes.ScriptError, result.Code);
    }

    [Fact]
    public void NodeExecutionContext_TryParseJson_Valid_ReturnsTrue()
    {
        var ctx = new NodeExecutionContext();

        var success = ctx.TryParseJson("{\"a\":1}", out var doc, out var errorCode);

        Assert.True(success);
        Assert.NotNull(doc);
        Assert.Null(errorCode);
        doc.Dispose();
    }

    [Fact]
    public void NodeExecutionContext_TryParseJson_Invalid_ReturnsFalse()
    {
        var ctx = new NodeExecutionContext();

        var success = ctx.TryParseJson("invalid", out var doc, out var errorCode);

        Assert.False(success);
        Assert.Equal("InvalidJson", errorCode);
    }

    [Fact]
    public void NodeExecutionContext_TryParseJsonT_Valid_ReturnsTrue()
    {
        var ctx = new NodeExecutionContext();

        var success = ctx.TryParseJson<Dictionary<string, int>>("{\"a\":1}", out var result, out var errorCode);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(1, result!["a"]);
        Assert.Null(errorCode);
    }

    [Fact]
    public void NodeExecutionContext_TryParseJsonT_Null_ReturnsFalse()
    {
        var ctx = new NodeExecutionContext();

        var success = ctx.TryParseJson<string>("null", out var result, out var errorCode);

        Assert.False(success);
        Assert.Equal("InvalidJson", errorCode);
    }

    [Fact]
    public void NodeExecutionContext_TryParseJsonT_Invalid_ReturnsFalse()
    {
        var ctx = new NodeExecutionContext();

        var success = ctx.TryParseJson<string>("invalid", out var result, out var errorCode);

        Assert.False(success);
        Assert.Equal("InvalidJson", errorCode);
    }

    private sealed class FakeCredentialAccessor : ICredentialAccessor
    {
        public CredentialValue? ById { get; set; }
        public CredentialValue? ByName { get; set; }
        public Exception? Throw { get; set; }

        public Task<CredentialValue> GetCredentialAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (Throw is not null) throw Throw;
            return Task.FromResult(ById ?? new CredentialValue());
        }

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            if (Throw is not null) throw Throw;
            return Task.FromResult(ByName);
        }
    }

    private sealed class FakeNodeExecutionLogger : IExecutionLogger
    {
        public void LogInformation(string message, params object?[] args) { }
        public void LogWarning(string message, params object?[] args) { }
        public void LogError(Exception? exception, string message, params object?[] args) { }
    }
}
