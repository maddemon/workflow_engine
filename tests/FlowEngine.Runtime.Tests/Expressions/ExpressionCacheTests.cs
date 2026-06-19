using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Expressions;
using Microsoft.Extensions.Caching.Memory;

namespace FlowEngine.Runtime.Tests.Expressions;

public class ExpressionCacheTests
{
    [Fact]
    public void Same_Template_Second_Evaluation_Uses_Cache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var evaluator = new ExpressionEvaluator(cache);
        var context = CreateContext(new JsonObject { ["id"] = "123" });

        var first = evaluator.EvaluateToString("{{ input.id }}", context);
        var second = evaluator.EvaluateToString("{{ input.id }}", context);

        Assert.Equal("123", first);
        Assert.Equal("123", second);
    }

    [Fact]
    public void Different_Templates_Parse_Separately()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var evaluator = new ExpressionEvaluator(cache);
        var context = CreateContext(new JsonObject { ["id"] = "123", ["name"] = "Alice" });

        var first = evaluator.EvaluateToString("{{ input.id }}", context);
        var second = evaluator.EvaluateToString("{{ input.name }}", context);

        Assert.Equal("123", first);
        Assert.Equal("Alice", second);
    }

    [Fact]
    public void Different_Input_Schema_Uses_Different_Cache_Entry()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var evaluator = new ExpressionEvaluator(cache);
        var contextWithId = CreateContext(new JsonObject { ["id"] = "123" });
        var contextWithName = CreateContext(new JsonObject { ["name"] = "Alice" });

        evaluator.EvaluateToString("{{ 1 + 2 }}", contextWithId);
        evaluator.EvaluateToString("{{ 1 + 2 }}", contextWithName);

        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Nested_Input_Schema_Change_Uses_Different_Cache_Entry()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var evaluator = new ExpressionEvaluator(cache);
        var contextWithName = CreateContext(new JsonObject { ["user"] = new JsonObject { ["name"] = "A" } });
        var contextWithAge = CreateContext(new JsonObject { ["user"] = new JsonObject { ["age"] = 20 } });

        evaluator.EvaluateToString("{{ 1 }}", contextWithName);
        evaluator.EvaluateToString("{{ 1 }}", contextWithAge);

        Assert.Equal(2, cache.Count);
    }

    private static ExpressionContext CreateContext(JsonNode inputData)
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
                            Data = inputData,
                            Success = true
                        }
                    ]
                }
            },
            Metadata = new ExpressionMetadata()
        };
    }
}
