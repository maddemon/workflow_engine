using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 计算器工具节点，作为 Agent 的工具执行数学计算。
/// </summary>
public sealed class CalculatorToolNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "calculatorTool";

    /// <inheritdoc />
    public string DisplayName => "Calculator Tool";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "calculator";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool Output", Direction = PortDirection.Output, Type = PortType.AgentTool }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get math expression from LLM input
            var expression = GetExpression(context);
            if (string.IsNullOrWhiteSpace(expression))
            {
                return Task.FromResult(context.ErrorResult("MissingExpression", "Math expression is required."));
            }

            // Evaluate expression using Jint
            using var js = JsEngine.Create();
            var result = js.Evaluate(expression);
            var value = JsEngine.ToClrValue(result);

            var outputBatch = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject
                        {
                            ["expression"] = expression,
                            ["result"] = value is double d ? JsonValue.Create(d) : JsonValue.Create(value?.ToString() ?? string.Empty)
                        },
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            };

            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = outputBatch
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(context.ErrorResult("CalculationError", $"Calculation failed: {ex.Message}"));
        }
    }

    private string? GetExpression(NodeExecutionContext context)
    {
        if (context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) && batch.Items.Count > 0)
        {
            var data = batch.Items[0].Data;
            if (data is JsonObject obj)
            {
                if (obj.TryGetPropertyValue("expression", out var exprVal))
                {
                    return exprVal?.ToString();
                }
                if (obj.TryGetPropertyValue("query", out var queryVal))
                {
                    return queryVal?.ToString();
                }
                if (obj.TryGetPropertyValue("math", out var mathVal))
                {
                    return mathVal?.ToString();
                }
            }
            else if (data is JsonValue val)
            {
                return val.ToString();
            }
        }

        // Check ResolvedParameters
        if (context.ResolvedParameters.TryGetValue("expression", out var paramExpr))
        {
            return paramExpr?.ToString();
        }

        return null;
    }
}
