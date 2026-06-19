using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Expressions;
using Microsoft.Extensions.Caching.Memory;

namespace FlowEngine.Runtime.Tests.Expressions;

public class ExpressionEvaluatorTests
{
    private readonly ExpressionEvaluator _evaluator = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void Evaluate_Input_Variable_Returns_Value()
    {
        var context = CreateContext(inputData: new JsonObject { ["id"] = "123" });

        var result = _evaluator.EvaluateToString("{{ input.id }}", context);

        Assert.Equal("123", result);
    }

    [Fact]
    public void Evaluate_Parameter_Variable_Returns_Value()
    {
        var context = CreateContext(parameters: new Dictionary<string, object>
        {
            ["method"] = "POST"
        });

        var result = _evaluator.EvaluateToString("{{ parameter.method }}", context);

        Assert.Equal("POST", result);
    }

    [Fact]
    public void Evaluate_Mixed_String_Replaces_Expression()
    {
        var context = CreateContext(inputData: new JsonObject { ["id"] = "123" });

        var result = _evaluator.EvaluateToString(
            "https://api.example.com/users/{{ input.id }}/orders",
            context);

        Assert.Equal("https://api.example.com/users/123/orders", result);
    }

    [Fact]
    public void Evaluate_Node_Output_Access_Returns_Value()
    {
        var nodeOutputs = new Dictionary<string, DataBatch>
        {
            ["GetUser"] = new()
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject { ["name"] = "Alice" },
                        Success = true
                    }
                ]
            }
        };

        var context = CreateContext(nodeOutputs: nodeOutputs);

        var result = _evaluator.EvaluateToString("{{ nodes[\"GetUser\"].data.name }}", context);

        Assert.Equal("Alice", result);
    }

    [Fact]
    public void Evaluate_Items_Function_Indexer_Returns_Value()
    {
        var nodeBatches = new Dictionary<string, DataBatch>
        {
            ["GetUser"] = new()
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject { ["status"] = "active" },
                        Success = true
                    }
                ]
            }
        };

        var context = CreateContext(nodeBatches: nodeBatches);

        var result = _evaluator.EvaluateToString("{{ items(\"GetUser\")[0].data.status }}", context);

        Assert.Equal("active", result);
    }

    [Fact]
    public void Evaluate_Arithmetic_Returns_Result()
    {
        var context = CreateContext();

        var result = _evaluator.EvaluateToString("{{ 1 + 2 * 3 }}", context);

        Assert.Equal("7", result);
    }

    [Fact]
    public void Evaluate_Comparison_Returns_Boolean_String()
    {
        var context = CreateContext(inputData: new JsonObject { ["age"] = 20 });

        var result = _evaluator.EvaluateToString("{{ input.age >= 18 }}", context);

        Assert.Equal("True", result);
    }

    [Fact]
    public void Evaluate_Ternary_Returns_Selected_Branch()
    {
        var context = CreateContext(inputData: new JsonObject { ["active"] = true });

        var result = _evaluator.EvaluateToString("{{ input.active ? \"on\" : \"off\" }}", context);

        Assert.Equal("on", result);
    }

    [Fact]
    public void Evaluate_Function_Length_Returns_Array_Length()
    {
        var context = CreateContext(inputData: new JsonObject
        {
            ["items"] = new JsonArray("a", "b", "c")
        });

        var result = _evaluator.EvaluateToString("{{ length(input.items) }}", context);

        Assert.Equal("3", result);
    }

    [Fact]
    public void Evaluate_Function_Upper_Returns_Uppercase()
    {
        var context = CreateContext();

        var result = _evaluator.EvaluateToString("{{ upper(\"hello\") }}", context);

        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void Evaluate_Field_Not_Found_Includes_Available_Fields()
    {
        var context = CreateContext(inputData: new JsonObject { ["id"] = "123" });

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ input.name }}", context));

        Assert.Equal(ExpressionErrorType.FieldNotFound, ex.Error.Type);
        Assert.Contains("id", ex.Error.AvailableFields);
    }

    [Fact]
    public void Evaluate_Environment_Not_In_Whitelist_Throws_Security_Violation()
    {
        var context = CreateContext(environmentWhitelist: new HashSet<string> { "ALLOWED_VAR" });

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ env.SECRET }}", context));

        Assert.Equal(ExpressionErrorType.SecurityViolation, ex.Error.Type);
    }

    [Fact]
    public void Evaluate_Syntax_Error_Includes_Position()
    {
        var context = CreateContext();

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString("{{ input. }}", context));

        Assert.Equal(ExpressionErrorType.SyntaxError, ex.Error.Type);
    }

    [Fact]
    public void Evaluate_Workflow_And_Execution_Returns_Metadata()
    {
        var workflowId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var workflow = new Workflow
        {
            Id = workflowId,
            Name = "TestWorkflow"
        };
        var context = CreateContext(workflow: workflow, executionId: executionId);

        var workflowName = _evaluator.EvaluateToString("{{ workflow.name }}", context);
        var workflowIdResult = _evaluator.EvaluateToString("{{ workflow.id }}", context);
        var executionIdResult = _evaluator.EvaluateToString("{{ execution.id }}", context);

        Assert.Equal("TestWorkflow", workflowName);
        Assert.Equal(workflowId.ToString(), workflowIdResult);
        Assert.Equal(executionId.ToString(), executionIdResult);
    }

    [Fact]
    public void Evaluate_Node_Output_Data_Always_Returns_First_Item()
    {
        var nodeOutputs = new Dictionary<string, DataBatch>
        {
            ["GetUser"] = new()
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject { ["name"] = "First" },
                        Success = true
                    },
                    new DataItem
                    {
                        Data = new JsonObject { ["name"] = "Second" },
                        Success = true
                    }
                ]
            }
        };

        var context = CreateContext(nodeOutputs: nodeOutputs, runIndex: 1);

        var result = _evaluator.EvaluateToString("{{ nodes[\"GetUser\"].data.name }}", context);

        Assert.Equal("First", result);
    }

    [Fact]
    public void Evaluate_Input_Uses_RunIndex()
    {
        var context = new ExpressionContext
        {
            Inputs = new Dictionary<string, DataBatch>
            {
                ["input"] = new()
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = new JsonObject { ["name"] = "Zero" },
                            Success = true
                        },
                        new DataItem
                        {
                            Data = new JsonObject { ["name"] = "One" },
                            Success = true
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object>(),
            NodeOutputs = new Dictionary<string, DataBatch>(),
            NodeBatches = new Dictionary<string, DataBatch>(),
            EnvironmentWhitelist = new HashSet<string>(),
            Metadata = new ExpressionMetadata { RunIndex = 1 }
        };

        var result = _evaluator.EvaluateToString("{{ input.name }}", context);

        Assert.Equal("One", result);
    }

    [Fact]
    public void Evaluate_Input_Port_By_Name_Returns_Current_Item()
    {
        var context = new ExpressionContext
        {
            Inputs = new Dictionary<string, DataBatch>
            {
                ["secondary"] = new()
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = new JsonObject { ["id"] = "456" },
                            Success = true
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object>(),
            NodeOutputs = new Dictionary<string, DataBatch>(),
            NodeBatches = new Dictionary<string, DataBatch>(),
            EnvironmentWhitelist = new HashSet<string>(),
            Metadata = new ExpressionMetadata()
        };

        var result = _evaluator.EvaluateToString("{{ inputs[\"secondary\"].id }}", context);

        Assert.Equal("456", result);
    }

    [Fact]
    public void Evaluate_Field_Not_Found_Includes_Template_In_Error()
    {
        var context = CreateContext(inputData: new JsonObject { ["id"] = "123" });
        const string template = "{{ input.name }}";

        var ex = Assert.Throws<ExpressionEvaluationException>(
            () => _evaluator.EvaluateToString(template, context));

        Assert.Equal(ExpressionErrorType.FieldNotFound, ex.Error.Type);
        Assert.Equal(template, ex.Error.Expression);
    }

    [Fact]
    public void Evaluate_Short_Circuit_And_Does_Not_Evaluate_Right()
    {
        var context = CreateContext();

        var result = _evaluator.EvaluateToString("{{ false && unknown.value }}", context);

        Assert.Equal("False", result);
    }

    [Fact]
    public void Evaluate_Short_Circuit_Or_Does_Not_Evaluate_Right()
    {
        var context = CreateContext();

        var result = _evaluator.EvaluateToString("{{ true || unknown.value }}", context);

        Assert.Equal("True", result);
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
