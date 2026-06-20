using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Expressions;
using Microsoft.Extensions.Caching.Memory;

namespace FlowEngine.Runtime.Tests.Expressions;

/// <summary>
/// 表达式求值器测试 —— 覆盖模板中的比较运算符场景。
/// </summary>
public class ExpressionEvaluatorComparisonTests
{
    private readonly ExpressionEvaluator _evaluator = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void Template_Equal_Numeric_Returns_True()
    {
        var context = CreateContext(inputData: new JsonObject { ["statusCode"] = 200 });

        var result = _evaluator.Evaluate("{{ input.statusCode }} == 200", context);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Template_Equal_Numeric_Returns_False()
    {
        var context = CreateContext(inputData: new JsonObject { ["statusCode"] = 404 });

        var result = _evaluator.Evaluate("{{ input.statusCode }} == 200", context);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Template_NotEqual_Returns_True()
    {
        var context = CreateContext(inputData: new JsonObject { ["status"] = "ok" });

        var result = _evaluator.Evaluate("{{ input.status }} != 'error'", context);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Template_GreaterThan_Returns_True()
    {
        var context = CreateContext(inputData: new JsonObject { ["count"] = 10 });

        var result = _evaluator.Evaluate("{{ input.count }} > 5", context);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Template_LessThanOrEqual_Returns_True()
    {
        var context = CreateContext(inputData: new JsonObject { ["score"] = 50 });

        var result = _evaluator.Evaluate("{{ input.score }} <= 100", context);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Template_Comparison_With_RightExpression_Returns_True()
    {
        var context = CreateContext(
            inputData: new JsonObject { ["a"] = 10 },
            parameters: new Dictionary<string, object> { ["threshold"] = 10 });

        var result = _evaluator.Evaluate("{{ input.a }} == {{ parameter.threshold }}", context);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Pure_Expression_Returns_Raw_Value()
    {
        var context = CreateContext(inputData: new JsonObject { ["count"] = 42 });

        var result = _evaluator.Evaluate("{{ input.count }}", context);

        Assert.Equal(42, result);
    }

    [Fact]
    public void Mixed_Template_Concatenates_As_String()
    {
        var context = CreateContext(inputData: new JsonObject { ["name"] = "test" });

        var result = _evaluator.Evaluate("item-{{ input.name }}-end", context);

        Assert.Equal("item-test-end", result);
    }

    private static ExpressionContext CreateContext(
        JsonObject? inputData = null,
        IReadOnlyDictionary<string, object>? parameters = null,
        IReadOnlyDictionary<string, DataBatch>? nodeOutputs = null)
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
            NodeOutputs = nodeOutputs ?? new Dictionary<string, DataBatch>(),
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
