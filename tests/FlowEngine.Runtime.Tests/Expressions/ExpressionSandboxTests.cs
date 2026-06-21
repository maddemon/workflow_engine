using System.Text.Json.Nodes;
using System.Threading;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Expressions;
using Microsoft.Extensions.Caching.Memory;

namespace FlowEngine.Runtime.Tests.Expressions;

public class ExpressionSandboxTests
{
    private readonly ExpressionEvaluator _evaluator = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void NonWhitelistFunction_Throws_SecurityViolation()
    {
        var context = CreateContext();

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ readFile(\"test.txt\") }}", context));

        Assert.Equal(ExpressionErrorType.SecurityViolation, ex.Error.Type);
        Assert.Contains("readFile", ex.Error.Expression);
    }

    [Fact]
    public void NonWhitelistFunction_System_Throws_FieldNotFound()
    {
        var context = CreateContext();

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ System.IO.File.ReadAllText(\"test.txt\") }}", context));

        Assert.Equal(ExpressionErrorType.FieldNotFound, ex.Error.Type);
    }

    [Fact]
    public void NonWhitelistFunction_Process_Throws_SecurityViolation()
    {
        var context = CreateContext();

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ startProcess(\"cmd\") }}", context));

        Assert.Equal(ExpressionErrorType.SecurityViolation, ex.Error.Type);
    }

    [Fact]
    public void WhitelistFunction_Length_Works()
    {
        var context = CreateContext(inputData: new JsonObject { ["items"] = new JsonArray("a", "b", "c") });

        var result = _evaluator.EvaluateToString("{{ length(input.items) }}", context);

        Assert.Equal("3", result);
    }

    [Fact]
    public void WhitelistFunction_Trim_Works()
    {
        var context = CreateContext(inputData: new JsonObject { ["name"] = "  hello  " });

        var result = _evaluator.EvaluateToString("{{ trim(input.name) }}", context);

        Assert.Equal("hello", result);
    }

    [Fact]
    public void WhitelistFunction_Upper_Works()
    {
        var context = CreateContext();

        var result = _evaluator.EvaluateToString("{{ upper(\"hello\") }}", context);

        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void WhitelistFunction_Lower_Works()
    {
        var context = CreateContext();

        var result = _evaluator.EvaluateToString("{{ lower(\"HELLO\") }}", context);

        Assert.Equal("hello", result);
    }

    [Fact]
    public void WhitelistFunction_Now_Works()
    {
        var context = CreateContext();

        var result = _evaluator.EvaluateToString("{{ now() }}", context);

        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void Environment_Whitelisted_Variable_Accessible()
    {
        Environment.SetEnvironmentVariable("TEST_API_URL", "https://api.example.com");
        try
        {
            var context = CreateContext(environmentWhitelist: new HashSet<string> { "TEST_API_URL" });

            var result = _evaluator.EvaluateToString("{{ env.TEST_API_URL }}", context);

            Assert.Equal("https://api.example.com", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_API_URL", null);
        }
    }

    [Fact]
    public void Environment_NonWhitelisted_Variable_Throws_SecurityViolation()
    {
        var context = CreateContext(environmentWhitelist: new HashSet<string> { "ALLOWED_VAR" });

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ env.DATABASE_PASSWORD }}", context));

        Assert.Equal(ExpressionErrorType.SecurityViolation, ex.Error.Type);
        Assert.Contains("DATABASE_PASSWORD", ex.Error.Expression);
        Assert.Contains("ALLOWED_VAR", ex.Error.AvailableFields);
    }

    [Fact]
    public void Depth_Limiting_Exceeded_Throws_SyntaxError()
    {
        var context = CreateContext();

        var parser = new ExpressionParser();
        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => parser.Parse("{{ true ? (true ? (true ? (true ? (true ? 1 : 0) : 0) : 0) : 0) : 0 }}", 3));

        Assert.Equal(ExpressionErrorType.SyntaxError, ex.Error.Type);
    }

    [Fact]
    public void Depth_Limiting_Within_Limit_Succeeds()
    {
        var context = CreateContext();

        var parser = new ExpressionParser();
        var segments = parser.Parse("{{ 1 + 2 }}", 10);

        Assert.Single(segments);
    }

    [Fact]
    public void CancellationToken_Cancelled_Throws_OperationCanceledException()
    {
        var context = CreateContext(inputData: new JsonObject { ["id"] = "123" });
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => _evaluator.EvaluateToString("{{ input.id }}", context, cts.Token));
    }

    [Fact]
    public void CancellationToken_During_Evaluation_Checks()
    {
        var context = CreateContext(inputData: new JsonObject { ["id"] = "123" });
        var cts = new CancellationTokenSource();

        var result = _evaluator.EvaluateToString("{{ input.id }}", context, cts.Token);

        Assert.Equal("123", result);
    }

    [Fact]
    public void JMESPath_Basic_Query()
    {
        var context = CreateContext(inputData: new JsonObject
        {
            ["users"] = new JsonArray(
                new JsonObject { ["name"] = "Alice", ["age"] = 30 },
                new JsonObject { ["name"] = "Bob", ["age"] = 15 }
            )
        });

        var result = _evaluator.EvaluateToString("{{ jmespath(input, \"users[0].name\") }}", context);

        Assert.Equal("Alice", result);
    }

    [Fact]
    public void JMESPath_Filter_Query()
    {
        var context = CreateContext(inputData: new JsonObject
        {
            ["users"] = new JsonArray(
                new JsonObject { ["name"] = "Alice", ["age"] = 30 },
                new JsonObject { ["name"] = "Bob", ["age"] = 15 }
            )
        });

        var result = _evaluator.EvaluateToString(
            "{{ jmespath(input, \"users[?age > `18`].name\") }}", context);

        Assert.Contains("Alice", result);
    }

    [Fact]
    public void JMESPath_Nested_Query()
    {
        var context = CreateContext(inputData: new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["items"] = new JsonArray("a", "b", "c")
            }
        });

        var result = _evaluator.EvaluateToString("{{ jmespath(input, \"data.items[0]\") }}", context);

        Assert.Equal("a", result);
    }

    [Fact]
    public void JMESPath_Invalid_Query_Throws_SyntaxError()
    {
        var context = CreateContext(inputData: new JsonObject { ["id"] = "123" });

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ jmespath(input, \"invalid[[[\") }}", context));

        Assert.Equal(ExpressionErrorType.SyntaxError, ex.Error.Type);
    }

    [Fact]
    public void JMESPath_Wrong_Argument_Count_Throws_TypeMismatch()
    {
        var context = CreateContext();

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ jmespath() }}", context));

        Assert.Equal(ExpressionErrorType.TypeMismatch, ex.Error.Type);
    }

    [Fact]
    public void JMESPath_Null_Query_Throws_FieldNotFound()
    {
        var context = CreateContext();

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ jmespath(input, null) }}", context));

        Assert.Equal(ExpressionErrorType.FieldNotFound, ex.Error.Type);
    }

    [Fact]
    public void AST_Cache_Hit_On_Same_Expression()
    {
        var context = CreateContext(inputData: new JsonObject { ["id"] = "123" });

        var result1 = _evaluator.EvaluateToString("{{ input.id }}", context);
        var result2 = _evaluator.EvaluateToString("{{ input.id }}", context);

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void AST_Cache_Invalidated_On_Schema_Change()
    {
        var context1 = CreateContext(inputData: new JsonObject { ["id"] = "123", ["name"] = "Alice" });
        var context2 = CreateContext(inputData: new JsonObject { ["id"] = "456" });

        var result1 = _evaluator.EvaluateToString("{{ input.id }}", context1);
        var result2 = _evaluator.EvaluateToString("{{ input.id }}", context2);

        Assert.Equal("123", result1);
        Assert.Equal("456", result2);
    }

    [Fact]
    public void SecurityViolation_Includes_Expression_Text()
    {
        var context = CreateContext();

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ readFile(\"test.txt\") }}", context));

        Assert.Contains("readFile", ex.Error.Expression);
        Assert.Contains("白名单", ex.Error.Reason);
    }

    private static ExpressionContext CreateContext(
        JsonNode? inputData = null,
        IReadOnlyDictionary<string, object>? parameters = null,
        IReadOnlyDictionary<string, DataBatch>? nodeOutputs = null,
        IReadOnlyDictionary<string, DataBatch>? nodeBatches = null,
        IReadOnlySet<string>? environmentWhitelist = null,
        Workflow? workflow = null,
        int runIndex = 0,
        Guid? executionId = null)
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
            NodeBatches = nodeBatches ?? new Dictionary<string, DataBatch>(),
            EnvironmentWhitelist = environmentWhitelist ?? new HashSet<string>(),
            Metadata = new ExpressionMetadata
            {
                Workflow = workflow,
                RunIndex = runIndex,
                ExecutionId = executionId ?? Guid.Empty
            }
        };
    }
}
