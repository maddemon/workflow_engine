using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// JavaScript 节点测试 —— 覆盖对象输出转换。
/// </summary>
public class JSNodeTests
{
    private readonly JSNode _node = new();

    [Fact]
    public async Task Execute_Returns_Object_As_JsonObject()
    {
        var (node, context) = CreateContext(code: "return { message: 'ok', statusCode: 200 }");

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message ?? "Unknown error");
        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);
        var json = data!.ToJsonString();
        Assert.Contains("\"message\":\"ok\"", json);
        Assert.Contains("\"statusCode\":200", json);
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
                Id = Guid.NewGuid(),
                TypeName = "script",
                Name = "Test JS",
                Parameters = new Dictionary<string, object> { ["code"] = code },
                Ports = [],
                ErrorStrategy = ErrorStrategy.Terminate,
            },
            ExecutionId = Guid.NewGuid(),
            Inputs = new Dictionary<string, DataBatch>
            {
                ["input"] = new()
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
