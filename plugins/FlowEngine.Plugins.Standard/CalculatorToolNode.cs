using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 计算器工具节点，作为 Agent 的工具执行数学计算。
/// </summary>
[NodeMeta(TypeName = "calculatorTool", DisplayName = "Calculator Tool", Category = NodeCategory.AI, Icon = "calculator", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
[Port(FlowConstants.PortNames.Tools, "Tool Output", PortDirection.Output, PortType.AgentTool)]
public sealed class CalculatorToolNode : NodeBase
{
    [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;

    /// <summary>
    /// 待计算表达式。JS 表达式，支持 <c>$json</c> / <c>$input</c>。示例：<c>1 + 2</c>。
    /// 输入批次读取之后回退到此属性（不再读取被合规规则禁止的已解析参数字典）。
    /// </summary>
    [Description("Math expression to evaluate. JS expression; supports $json/$input. Example: 1 + 2")]
    public string Expression { get; set; } = string.Empty;

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            var expression = GetExpression(input);
            if (string.IsNullOrWhiteSpace(expression))
            {
                throw new NodeExecutionException("MissingExpression", "Math expression is required.");
            }

            var script = new Script
            {
                Source = expression,
                Language = ScriptLanguage.JavaScript,
                ReturnType = ScriptReturnType.Number
            };
            var evalItem = input.InputBatch.Items.Count > 0 ? input.InputBatch.Items[0].Data : null;
            var value = await script.EvaluateAsync<object>(Ctx, item: evalItem, itemIndex: 0, cancellationToken: ct).ConfigureAwait(false);

            var outputBatch = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject
                        {
                            ["expression"] = expression,
                            ["result"] = value switch
                            {
                                double d => JsonValue.Create(d),
                                int i => JsonValue.Create(i),
                                long l => JsonValue.Create(l),
                                decimal m => JsonValue.Create(m),
                                float f => JsonValue.Create(f),
                                _ => JsonValue.Create(value?.ToString() ?? string.Empty)
                            }
                        },
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            };

            return NodeHandlerOutput.Data(outputBatch);
        }
        catch (OperationCanceledException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "Calculation was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.ScriptError, $"Expression evaluation failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            throw new NodeExecutionException("CalculationError", $"Calculation failed: {ex.Message}");
        }
    }

    private string? GetExpression(NodeInput input)
    {
        var batch = input.InputBatch;
        if (batch.Items.Count > 0)
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

        // 回退到绑定属性（不读取被合规规则禁止的已解析参数字典）。
        if (!string.IsNullOrWhiteSpace(Expression))
        {
            return Expression;
        }

        return null;
    }
}
