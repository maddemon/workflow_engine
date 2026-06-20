using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Expressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Expressions;

/// <summary>
/// 参数解析器测试 —— 覆盖 JsonElement 类型值的表达式求值。
/// </summary>
public class ParameterResolverTests
{
    private readonly ParameterResolver _resolver;

    public ParameterResolverTests()
    {
        var evaluator = new ExpressionEvaluator(new MemoryCache(new MemoryCacheOptions()));
        _resolver = new ParameterResolver(evaluator, NullLogger<ParameterResolver>.Instance);
    }

    [Fact]
    public void Resolve_String_Parameter_Evaluates_Expression()
    {
        var context = CreateContext(inputData: new JsonObject { ["statusCode"] = 200 });
        var raw = new Dictionary<string, object>
        {
            ["condition"] = "{{ input.statusCode }} == 200"
        };

        var result = _resolver.Resolve(raw, context);

        Assert.Equal(true, result["condition"]);
    }

    [Fact]
    public void Resolve_JsonElement_String_Evaluates_Expression()
    {
        var context = CreateContext(inputData: new JsonObject { ["statusCode"] = 200 });

        // Simulate JSON deserialization: string values become JsonElement
        var jsonStr = """{"condition": "{{ input.statusCode }} == 200"}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;

        var rawAsObjects = raw.ToDictionary(
            kv => kv.Key,
            kv => (object)kv.Value);

        var result = _resolver.Resolve(rawAsObjects, context);

        Assert.Equal(true, result["condition"]);
    }

    [Fact]
    public void Resolve_JsonElement_NonString_Passes_Through()
    {
        var context = CreateContext();
        var jsonStr = """{"count": 42}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var rawAsObjects = raw.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        var result = _resolver.Resolve(rawAsObjects, context);

        Assert.Equal(42.0, Convert.ToDouble(result["count"]));
    }

    [Fact]
    public void Resolve_Empty_JsonElement_String_Returns_Empty()
    {
        var context = CreateContext();
        var jsonStr = """{"url": ""}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var rawAsObjects = raw.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        var result = _resolver.Resolve(rawAsObjects, context);

        Assert.Equal("", result["url"]);
    }

    private static ExpressionContext CreateContext(
        JsonObject? inputData = null,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        return new ExpressionContext
        {
            Inputs = new Dictionary<string, DataBatch>
            {
                ["input"] = new()
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = inputData ?? new JsonObject(),
                            Success = true
                        }
                    ]
                }
            },
            RawParameters = parameters ?? new Dictionary<string, object>(),
            NodeOutputs = new Dictionary<string, DataBatch>(),
            NodeBatches = new Dictionary<string, DataBatch>(),
            EnvironmentWhitelist = new HashSet<string>(),
            Metadata = new ExpressionMetadata
            {
                Workflow = null,
                RunIndex = 0,
                ExecutionId = Guid.Empty
            }
        };
    }
}
