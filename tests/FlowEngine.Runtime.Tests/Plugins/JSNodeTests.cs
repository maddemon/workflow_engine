using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// JavaScript 节点测试 —— 覆盖对象输出转换。
/// 迁移为 NodeBase 后，经 <c>((INodeType)node).ExecuteAsync</c> 走适配层。
/// </summary>
public class JSNodeTests
{
    private readonly JSNode _node = new();

    private static Task<NodeExecutionResult> RunAsync(JSNode node, NodeExecutionContext context, CancellationToken ct = default)
        => ((INodeType)node).ExecuteAsync(context, ct);

    [Fact]
    public async Task Execute_Returns_Object_As_JsonObject()
    {
        var (node, context) = CreateContext(code: "return { message: 'ok', statusCode: 200 }");

        var result = await RunAsync(node, context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message ?? "Unknown error");
        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);
        var json = data!.ToJsonString();
        Assert.Contains("\"message\":\"ok\"", json);
        Assert.Contains("\"statusCode\":200", json);
    }

    [Fact]
    public async Task Execute_Returns_Array_Expands_To_Multiple_Items()
    {
        var (node, context) = CreateContext(code: "return [{ id: 1 }, { id: 2 }]");

        var result = await RunAsync(node, context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message ?? "Unknown error");
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Contains("\"id\":1", result.Output.Items[0].Data!.ToJsonString());
        Assert.Contains("\"id\":2", result.Output.Items[1].Data!.ToJsonString());
    }

    [Fact]
    public async Task Execute_Reads_Input_First_As_Object()
    {
        var inputData = new JsonObject
        {
            ["greeting"] = "Hello from Flow Engine!"
        };
        var (node, context) = CreateContext(
            code: "const first = $input.first();\nreturn { message: first.greeting, status: 'success' };",
            inputData: inputData);

        var result = await RunAsync(node, context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message ?? "Unknown error");
        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);
        var json = data!.ToJsonString();
        Assert.Contains("\"message\":\"Hello from Flow Engine!\"", json);
        Assert.Contains("\"status\":\"success\"", json);
    }

    private static (JSNode Node, NodeExecutionContext Context) CreateContext(
        string code,
        JsonObject? inputData = null)
    {
        var node = new JSNode { Code = code };
        var context = new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = "Test JS",
                TypeName = "script",
                Name = "Test JS",
                Parameters = new Dictionary<string, object> { ["code"] = code },
                Ports = [],
                ErrorStrategy = ErrorStrategy.Terminate,
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
                            Data = inputData ?? new JsonObject(),
                            Success = true,
                            SourceIndex = 0,
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object> { ["code"] = code },
            ResolvedParameters = new Dictionary<string, object> { ["code"] = code },
            CancellationToken = CancellationToken.None,
        };

        return (node, context);
    }
}
