using FlowEngine.Core;
using FlowEngine.Core.Scripting;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 数据质量校验节点，支持多种校验规则。校验失败时按 ErrorStrategy 处理。
/// </summary>
public sealed class DataQualityNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "dataQuality";

    /// <inheritdoc />
    public string DisplayName => "Data Quality";

    /// <inheritdoc />
    public string Category => "Data";

    /// <inheritdoc />
    public string Icon => "shield-check";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 校验规则列表（JSON 数组）。每项包含 type + 参数。
    /// 支持类型：rowCount(min,max), fieldNotNull(field), fieldPattern(field,pattern), fieldRange(field,min,max), customExpression(expression)
    /// 示例：[{"type":"rowCount","min":1},{"type":"fieldNotNull","field":"email"}]
    /// </summary>
    [DisplayName("Rules")]
    [Description("Validation rules as JSON array. Each rule has type + params. Example: [{\"type\":\"rowCount\",\"min\":1}]")]
    [Hint(PresentationHint.Expression)]
    public string Rules { get; set; } = "[]";

    /// <summary>
    /// 校验失败时是否仍然传递数据到下游。
    /// </summary>
    [DisplayName("Pass On Failure")]
    [Description("Whether to pass data through on validation failure. When false, validation failure blocks data flow.")]
    public bool PassOnFailure { get; set; } = false;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        // 1. Get input data
        var inputBatch = context.GetInputBatch();

        var itemCount = inputBatch.Items.Count;

        // 2. Parse rules
        List<JsonElement>? rulesList;
        if (!context.TryParseJson(Rules, out var rulesDoc, out _))
        {
            return Task.FromResult(context.ErrorResult("InvalidRules", "Rules JSON 格式无效。"));
        }
        // 注意：JsonElement 引用 JsonDocument 的池化内存，文档 dispose 后不可访问。
        // 此处不提前 dispose，保持文档存活至方法结束，由 GC 回收。
        rulesList = rulesDoc.RootElement.EnumerateArray().ToList();

        // 3. Validate each rule
        var failures = new List<JsonObject>();
        var passedCount = 0;

        foreach (var rule in rulesList)
        {
            var type = rule.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            if (string.IsNullOrEmpty(type)) continue;

            var (passed, message) = type switch
            {
                "rowCount" => ValidateRowCount(rule, itemCount),
                "fieldNotNull" => ValidateFieldNotNull(rule, inputBatch),
                "fieldPattern" => ValidateFieldPattern(rule, inputBatch),
                "fieldRange" => ValidateFieldRange(rule, inputBatch),
                "customExpression" => ValidateCustomExpression(rule, inputBatch),
                _ => (false, $"未知校验规则类型: {type}")
            };

            if (passed)
            {
                passedCount++;
            }
            else
            {
                failures.Add(new JsonObject
                {
                    ["type"] = type,
                    ["message"] = message
                });
            }
        }

        // 4. Build report
        var report = new JsonObject
        {
            ["totalRules"] = rulesList.Count,
            ["passedRules"] = passedCount,
            ["failedRules"] = failures.Count,
            ["inputItemCount"] = itemCount,
            ["failures"] = new JsonArray(failures.Cast<JsonNode>().ToArray())
        };

        // 5. Determine result
        var hasFailures = failures.Count > 0;

        if (hasFailures && !PassOnFailure)
        {
            // Block data flow, return error with report
            var output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = report,
                        Success = false,
                        Error = new NodeError
                        {
                            Code = "DataQualityCheckFailed",
                            Message = $"数据质量校验失败：{failures.Count}/{rulesList.Count} 条规则未通过。",
                            NodeDefinitionId = context.Node.Id,
                            Details = new Dictionary<string, string>
                            {
                                ["failedRules"] = failures.Count.ToString(),
                                ["totalRules"] = rulesList.Count.ToString()
                            }
                        }
                    }
                ]
            };

            return Task.FromResult(new NodeExecutionResult
            {
                Success = false,
                Output = output,
                Error = new NodeError
                {
                    Code = "DataQualityCheckFailed",
                    Message = $"数据质量校验失败：{failures.Count}/{rulesList.Count} 条规则未通过。",
                    NodeDefinitionId = context.Node.Id
                }
            });
        }

        // Pass data through (either all passed, or PassOnFailure=true)
        // Attach report to each item's data
        var outputItems = inputBatch.Items.Select(item =>
        {
            var mergedData = item.Data is JsonObject obj
                ? obj.DeepClone()
                : new JsonObject { ["_original"] = item.Data };
            mergedData["_dqReport"] = report.DeepClone();
            return new DataItem
            {
                Data = mergedData,
                Success = item.Success,
                Error = item.Error,
                SourceIndex = item.SourceIndex
            };
        }).ToList();

        return Task.FromResult(new NodeExecutionResult
        {
            Success = !hasFailures,
            Output = new DataBatch { Items = outputItems }
        });
    }

    private static (bool passed, string message) ValidateRowCount(JsonElement rule, int itemCount)
    {
        var min = rule.TryGetProperty("min", out var minProp) ? minProp.GetInt32() : 0;
        var max = rule.TryGetProperty("max", out var maxProp) ? (int?)maxProp.GetInt32() : null;

        if (itemCount < min)
            return (false, $"行数 {itemCount} < 最小值 {min}");
        if (max.HasValue && itemCount > max.Value)
            return (false, $"行数 {itemCount} > 最大值 {max.Value}");

        return (true, string.Empty);
    }

    private static (bool passed, string message) ValidateFieldNotNull(JsonElement rule, DataBatch batch)
    {
        var field = rule.TryGetProperty("field", out var fProp) ? fProp.GetString() : null;
        if (string.IsNullOrEmpty(field))
            return (false, "fieldNotNull 规则缺少 field 参数");

        var nullCount = batch.Items.Count(item =>
        {
            var value = JsonPath.GetValue(item.Data, field);
            return value is null;
        });

        if (nullCount > 0)
            return (false, $"字段 '{field}' 有 {nullCount} 条记录为空");

        return (true, string.Empty);
    }

    private static (bool passed, string message) ValidateFieldPattern(JsonElement rule, DataBatch batch)
    {
        var field = rule.TryGetProperty("field", out var fProp) ? fProp.GetString() : null;
        var pattern = rule.TryGetProperty("pattern", out var pProp) ? pProp.GetString() : null;

        if (string.IsNullOrEmpty(field))
            return (false, "fieldPattern 规则缺少 field 参数");
        if (string.IsNullOrEmpty(pattern))
            return (false, "fieldPattern 规则缺少 pattern 参数");

        try
        {
            // 使用 RegexOptions.NonBacktracking 防止 ReDoS 攻击（.NET 7+）
            var regex = new Regex(pattern, RegexOptions.NonBacktracking, TimeSpan.FromSeconds(5));
            var mismatchCount = batch.Items.Count(item =>
            {
                var value = JsonPath.GetValue(item.Data, field);
                return value is null || !regex.IsMatch(value);
            });

            if (mismatchCount > 0)
                return (false, $"字段 '{field}' 有 {mismatchCount} 条记录不匹配正则 '{pattern}'");

            return (true, string.Empty);
        }
        catch (ArgumentException ex)
        {
            return (false, $"正则表达式无效: {ex.Message}");
        }
        catch (RegexMatchTimeoutException)
        {
            return (false, $"正则匹配超时: '{pattern}'");
        }
    }

    private static (bool passed, string message) ValidateFieldRange(JsonElement rule, DataBatch batch)
    {
        var field = rule.TryGetProperty("field", out var fProp) ? fProp.GetString() : null;
        var min = rule.TryGetProperty("min", out var minProp) ? (double?)minProp.GetDouble() : null;
        var max = rule.TryGetProperty("max", out var maxProp) ? (double?)maxProp.GetDouble() : null;

        if (string.IsNullOrEmpty(field))
            return (false, "fieldRange 规则缺少 field 参数");

        var outOfRangeCount = batch.Items.Count(item =>
        {
            var valueStr = JsonPath.GetValue(item.Data, field);
            if (valueStr is null || !double.TryParse(valueStr, out var numValue))
                return true; // Non-numeric treated as out of range

            if (min.HasValue && numValue < min.Value) return true;
            if (max.HasValue && numValue > max.Value) return true;
            return false;
        });

        if (outOfRangeCount > 0)
        {
            var rangeDesc = (min.HasValue, max.HasValue) switch
            {
                (true, true) => $"[{min}, {max}]",
                (true, false) => $"[>= {min}]",
                (false, true) => $"[<= {max}]",
                _ => "any"
            };
            return (false, $"字段 '{field}' 有 {outOfRangeCount} 条记录不在范围 {rangeDesc} 内");
        }

        return (true, string.Empty);
    }

    private static (bool passed, string message) ValidateCustomExpression(JsonElement rule, DataBatch batch)
    {
        // Custom expression validation - for now, basic expression support
        // The expression is evaluated for each item; it should return truthy/falsy
        var expression = rule.TryGetProperty("expression", out var eProp) ? eProp.GetString() : null;
        if (string.IsNullOrEmpty(expression))
            return (false, "customExpression 规则缺少 expression 参数");

        // customExpression 暂未支持实际求值，返回失败避免静默通过
        return (false, "customExpression 暂未支持");
    }

}
