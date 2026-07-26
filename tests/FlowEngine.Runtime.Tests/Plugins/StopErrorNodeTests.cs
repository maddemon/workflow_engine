using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// stopError 节点测试：覆盖错误码/消息返回、默认错误码、表达式求值、空消息、不抛异常、端口约束。
/// </summary>
public sealed class StopErrorNodeTests
{
    [Fact]
    public async Task ExecuteAsync_LiteralMessage_ReturnsErrorWithGivenCodeAndMessage()
    {
        var node = new StopErrorNode
        {
            ErrorMessage = new Script { Source = "Operation halted due to invalid state", ReturnType = ScriptReturnType.String },
            ErrorCode = "InvalidState"
        };

        var context = CreateContext(new JsonObject { ["id"] = 1 });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal("InvalidState", result.Error!.Code);
        Assert.Equal("Operation halted due to invalid state", result.Error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultErrorCode_WhenOmitted()
    {
        var node = new StopErrorNode
        {
            ErrorMessage = new Script { Source = "boom", ReturnType = ScriptReturnType.String }
        };

        var context = CreateContext(new JsonObject { ["id"] = 1 });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("StopAndError", result.Error!.Code);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyErrorCode_FallsBackToDefault()
    {
        var node = new StopErrorNode
        {
            ErrorMessage = new Script { Source = "boom", ReturnType = ScriptReturnType.String },
            ErrorCode = "   "
        };

        var context = CreateContext(new JsonObject { ["id"] = 1 });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("StopAndError", result.Error!.Code);
    }

    [Fact]
    public async Task ExecuteAsync_ExpressionMessage_EvaluatesInput()
    {
        var node = new StopErrorNode
        {
            ErrorMessage = new Script { Source = "\"stopped: \" + $json.reason", ReturnType = ScriptReturnType.String },
            ErrorCode = "DomainError"
        };

        var context = CreateContext(new JsonObject { ["reason"] = "bad input" });
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DomainError", result.Error!.Code);
        Assert.Equal("stopped: bad input", result.Error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyMessage_HandledWithoutThrowing()
    {
        var node = new StopErrorNode
        {
            ErrorMessage = Script.Empty,
            ErrorCode = "ExplicitCode"
        };

        var context = CreateContext(new JsonObject { ["id"] = 1 });

        Exception? thrown = null;
        NodeExecutionResult? result = null;
        try
        {
            result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.Null(thrown);
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal("ExplicitCode", result.Error!.Code);
        Assert.Equal(string.Empty, result.Error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotThrow_ReturnsErrorResult()
    {
        var node = new StopErrorNode
        {
            ErrorMessage = new Script { Source = "fail", ReturnType = ScriptReturnType.String },
            ErrorCode = "TestCode"
        };

        var context = CreateContext(new JsonObject { ["id"] = 1 });

        var thrown = await Record.ExceptionAsync(() => ((INodeType)node).ExecuteAsync(context, CancellationToken.None));

        Assert.Null(thrown);
    }

    [Fact]
    public void Ports_ContainsOnlyInput_NoOutput()
    {
        var node = new StopErrorNode();

        Assert.Single(((INodeType)node).Ports);
        Assert.Equal(FlowConstants.PortNames.Input, ((INodeType)node).Ports[0].Name);
        Assert.Equal(PortDirection.Input, ((INodeType)node).Ports[0].Direction);
        Assert.DoesNotContain(((INodeType)node).Ports, p => p.Direction == PortDirection.Output);
    }

    [Fact]
    public void Contract_IsStable_TypeNameCategoryIcon_Unchanged()
    {
        var node = new StopErrorNode();

        Assert.Equal("stopError", ((INodeType)node).TypeName);
        Assert.Equal("Flow", ((INodeType)node).Category);
        Assert.Equal("alert", ((INodeType)node).Icon);
        Assert.False(((INodeType)node).DefaultIsEntry);
    }

    private static NodeExecutionContext CreateContext(JsonObject inputData)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = "stopError1",
                TypeName = "stopError",
                Name = "Stop and Error",
                Parameters = new Dictionary<string, object>(),
                Ports = []
            },
            ExecutionId = Guid.NewGuid(),
            Inputs = new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new()
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = inputData,
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = new NullAccessor(),
            ScriptCache = new ScriptCache(Options.Create(new JsEngineOptions())),
            EngineOptions = new JsEngineOptions(),
            Logger = NullExecutionLogger.Instance,
            CancellationToken = CancellationToken.None
        };
    }

    private sealed class NullAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken ct = default) =>
            Task.FromResult<CredentialValue>(null!);

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult<CredentialValue?>(null);
    }

    private sealed class NullExecutionLogger : IExecutionLogger
    {
        public static readonly NullExecutionLogger Instance = new();

        public void LogInformation(string message, params object?[] args) { }

        public void LogWarning(string message, params object?[] args) { }

        public void LogError(Exception? exception, string message, params object?[] args) { }
    }
}
