using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using DevLab.JmesPath;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Expressions.Ast;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;

namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 表达式求值器。
/// </summary>
public sealed class ExpressionEvaluator
{
    private readonly IMemoryCache _cache;

    /// <summary>
    /// 初始化 <see cref="ExpressionEvaluator"/>。
    /// </summary>
    /// <param name="cache">内存缓存。</param>
    public ExpressionEvaluator(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    /// 求值模板字符串并返回字符串结果。
    /// </summary>
    /// <param name="template">模板字符串。</param>
    /// <param name="context">求值上下文。</param>
    /// <returns>求值后的字符串。</returns>
    public string EvaluateToString(string template, ExpressionContext context)
    {
        return EvaluateToString(template, context, CancellationToken.None);
    }

    /// <summary>
    /// 求值模板字符串并返回字符串结果。
    /// </summary>
    /// <param name="template">模板字符串。</param>
    /// <param name="context">求值上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>求值后的字符串。</returns>
    public string EvaluateToString(string template, ExpressionContext context, CancellationToken cancellationToken)
    {
        var result = Evaluate(template, context, cancellationToken);
        return result?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// 求值模板字符串并返回原始对象结果。
    /// </summary>
    /// <param name="template">模板字符串。</param>
    /// <param name="context">求值上下文。</param>
    /// <returns>求值结果。</returns>
    public object? Evaluate(string template, ExpressionContext context)
    {
        return Evaluate(template, context, CancellationToken.None);
    }

    /// <summary>
    /// 求值模板字符串并返回原始对象结果。
    /// </summary>
    /// <param name="template">模板字符串。</param>
    /// <param name="context">求值上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>求值结果。</returns>
    public object? Evaluate(string template, ExpressionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = CreateCacheKey(template, context);
        if (!_cache.TryGetValue(cacheKey, out IReadOnlyList<Segment>? segments) || segments is null)
        {
            segments = new ExpressionParser().Parse(template);
            _cache.Set(cacheKey, segments, TimeSpan.FromMinutes(10));
        }

        try
        {
            if (segments.Count == 1 && segments[0] is ExpressionSegment singleExpression)
            {
                return EvaluateExpression(singleExpression.Expression, context, cancellationToken);
            }

            var sb = new StringBuilder();
            for (var i = 0; i < segments.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var segment = segments[i];

                if (segment is ExpressionSegment expression)
                {
                    var value = EvaluateExpression(expression.Expression, context, cancellationToken);

                    // Check if followed by a comparison operator in the next literal segment
                    // e.g. {{ input.statusCode }} == 200 → parse as binary expression
                    if (i + 1 < segments.Count && segments[i + 1] is LiteralSegment nextLiteral)
                    {
                        var comparisonResult = TryParseComparisonFromLiteral(value, nextLiteral.Text, context, segments, ref i, cancellationToken);
                        if (comparisonResult.HasValue)
                        {
                            return comparisonResult.Value;
                        }
                    }

                    sb.Append(ConvertToString(value));
                }
                else if (segment is LiteralSegment literal)
                {
                    sb.Append(literal.Text);
                }
            }

            return sb.ToString();
        }
        catch (ExpressionEvaluationException ex)
        {
            throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ex.Error.Type,
                Expression = template,
                Position = ex.Error.Position,
                Reason = ex.Error.Reason,
                AvailableFields = ex.Error.AvailableFields
            });
        }
    }

    private static ExpressionCacheKey CreateCacheKey(string template, ExpressionContext context)
    {
        var inputSchemaHash = ComputeInputSchemaHash(context.Inputs);
        var parameterSchemaHash = ComputeParameterSchemaHash(context.RawParameters);
        return new ExpressionCacheKey(template, inputSchemaHash, parameterSchemaHash);
    }

    private static string ComputeInputSchemaHash(IReadOnlyDictionary<string, DataBatch> inputs)
    {
        var builder = new StringBuilder();
        foreach (var (portName, batch) in inputs.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            builder.Append(portName).Append(':');
            foreach (var item in batch.Items)
            {
                AppendJsonSchema(builder, item.Data);
            }
        }

        return builder.Length == 0 ? string.Empty : builder.ToString();
    }

    private static void AppendJsonSchema(StringBuilder builder, JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            builder.Append('{');
            foreach (var property in jsonObject.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                builder.Append(property.Key).Append('=');
                AppendJsonSchema(builder, property.Value);
                builder.Append(';');
            }

            builder.Append('}');
        }
        else if (node is JsonArray jsonArray)
        {
            builder.Append('[');
            foreach (var item in jsonArray)
            {
                AppendJsonSchema(builder, item);
            }

            builder.Append(']');
        }
        else if (node is JsonValue)
        {
            builder.Append(node.GetValueKind());
        }
        else
        {
            builder.Append('?');
        }
    }

    private static string ComputeParameterSchemaHash(IReadOnlyDictionary<string, object> parameters)
    {
        if (parameters.Count == 0)
        {
            return string.Empty;
        }

        var keys = parameters.Keys.Order(StringComparer.Ordinal);
        return string.Join(",", keys);
    }

    private static IReadOnlyDictionary<string, object?> CreateWorkflowDictionary(Workflow? workflow)
    {
        if (workflow is null)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = workflow.Id,
            ["name"] = workflow.Name,
            ["projectId"] = workflow.ProjectId,
            ["version"] = workflow.Version,
            ["isActive"] = workflow.IsActive
        };
    }

    /// <summary>
    /// 求值单个表达式节点。
    /// </summary>
    /// <param name="node">表达式节点。</param>
    /// <param name="context">求值上下文。</param>
    /// <returns>求值结果。</returns>
    public object? EvaluateExpression(ExpressionNode node, ExpressionContext context)
    {
        return EvaluateExpression(node, context, CancellationToken.None);
    }

    /// <summary>
    /// 求值单个表达式节点。
    /// </summary>
    /// <param name="node">表达式节点。</param>
    /// <param name="context">求值上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>求值结果。</returns>
    public object? EvaluateExpression(ExpressionNode node, ExpressionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return node switch
        {
            LiteralNode literal => literal.Value,
            IdentifierNode identifier => ResolveIdentifier(identifier.Name, context),
            MemberAccessNode memberAccess => EvaluateMemberAccess(memberAccess, context, cancellationToken),
            IndexerNode indexer => EvaluateIndexer(indexer, context, cancellationToken),
            FunctionCallNode functionCall => EvaluateFunctionCall(functionCall, context, cancellationToken),
            BinaryOperationNode binary => EvaluateBinary(binary, context, cancellationToken),
            UnaryOperationNode unary => EvaluateUnary(unary, context, cancellationToken),
            TernaryNode ternary => EvaluateTernary(ternary, context, cancellationToken),
            _ => throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.SyntaxError,
                Reason = $"不支持的表达式节点类型：{node.GetType().Name}。"
            })
        };
    }

    private object? ResolveIdentifier(string name, ExpressionContext context)
    {
        return name.ToLowerInvariant() switch
        {
            "input" => context.GetCurrentItem("input"),
            "inputs" => context.Inputs,
            "parameter" => context.RawParameters,
            "nodes" => new NodeOutputsWrapper(context.NodeOutputs),
            "items" => context.NodeBatches,
            "env" => new EnvironmentWrapper(context.EnvironmentWhitelist),
            "workflow" => CreateWorkflowDictionary(context.Metadata.Workflow),
            "execution" => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = context.Metadata.ExecutionId
            },
            "runindex" => context.Metadata.RunIndex,
            "run_index" => context.Metadata.RunIndex,
            "now" => context.Metadata.Now,
            _ => throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.FieldNotFound,
                Expression = name,
                Reason = $"未识别的变量 '{name}'。",
                AvailableFields =
                [
                    "input", "inputs", "parameter", "nodes", "items", "env", "workflow", "execution",
                    "runIndex", "now"
                ]
            })
        };
    }

    private object? EvaluateMemberAccess(MemberAccessNode memberAccess, ExpressionContext context, CancellationToken cancellationToken)
    {
        var target = EvaluateExpression(memberAccess.Target, context, cancellationToken);

        if (target is EnvironmentWrapper wrapper)
        {
            return wrapper.GetMember(memberAccess.MemberName);
        }

        if (target is DataBatch batch)
        {
            return ResolveDataBatchMember(batch, memberAccess.MemberName);
        }

        if (target is DataItem dataItem)
        {
            return ResolveDataItemMember(dataItem, memberAccess.MemberName);
        }

        return ValueAccessor.GetMember(target, memberAccess.MemberName, out _);
    }

    private static object? ResolveDataBatchMember(DataBatch batch, string memberName)
    {
        return memberName.ToLowerInvariant() switch
        {
            "data" => GetDataBatchFirstData(batch),
            "items" => batch.Items,
            _ => throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.FieldNotFound,
                Expression = $"DataBatch.{memberName}",
                Reason = $"数据批次中不存在成员 '{memberName}'。",
                AvailableFields = ["data", "items"]
            })
        };
    }

    private static object? GetDataBatchFirstData(DataBatch batch)
    {
        if (batch.Items.Count == 0)
        {
            return null;
        }

        var item = batch.Items[0];
        return item.Data is JsonObject jsonObject
            ? jsonObject
            : item.Data;
    }

    private static object? ResolveDataItemMember(DataItem item, string memberName)
    {
        return memberName.ToLowerInvariant() switch
        {
            "data" => item.Data is JsonObject jsonObject ? jsonObject : item.Data,
            "success" => item.Success,
            "error" => item.Error,
            "sourceindex" or "source_index" => item.SourceIndex,
            _ => throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.FieldNotFound,
                Expression = $"DataItem.{memberName}",
                Reason = $"数据项中不存在成员 '{memberName}'。",
                AvailableFields = ["data", "success", "error", "sourceIndex"]
            })
        };
    }

    private object? EvaluateIndexer(IndexerNode indexer, ExpressionContext context, CancellationToken cancellationToken)
    {
        var target = EvaluateExpression(indexer.Target, context, cancellationToken);
        var index = EvaluateExpression(indexer.Index, context, cancellationToken);

        if (target is NodeOutputsWrapper nodeOutputs)
        {
            var nodeName = Convert.ToString(index) ?? string.Empty;
            if (!nodeOutputs.Outputs.TryGetValue(nodeName, out var batch))
            {
                throw new ExpressionEvaluationException(new ExpressionError
                {
                    Type = ExpressionErrorType.NodeOutputNotFound,
                    Expression = $"nodes[\"{nodeName}\"]",
                    Reason = $"节点 '{nodeName}' 的输出不存在。",
                    AvailableFields = nodeOutputs.Outputs.Keys.ToList()
                });
            }

            return batch;
        }

        if (target is IReadOnlyDictionary<string, DataBatch> inputs)
        {
            var portName = Convert.ToString(index) ?? string.Empty;
            if (!inputs.ContainsKey(portName))
            {
                throw new ExpressionEvaluationException(new ExpressionError
                {
                    Type = ExpressionErrorType.FieldNotFound,
                    Expression = $"inputs[\"{portName}\"]",
                    Reason = $"输入端口 '{portName}' 不存在。",
                    AvailableFields = inputs.Keys.ToList()
                });
            }

            return context.GetCurrentItem(portName);
        }

        if (target is DataBatch dataBatch)
        {
            var intIndex = Convert.ToInt32(index);
            if (intIndex < 0 || intIndex >= dataBatch.Items.Count)
            {
                throw new ExpressionEvaluationException(new ExpressionError
                {
                    Type = ExpressionErrorType.TypeMismatch,
                    Reason = $"批次索引 {intIndex} 越界。"
                });
            }

            return dataBatch.Items[intIndex];
        }

        return ValueAccessor.GetIndex(target, index);
    }

    private object? EvaluateFunctionCall(FunctionCallNode functionCall, ExpressionContext context, CancellationToken cancellationToken)
    {
        var args = functionCall.Arguments.Select(a => EvaluateExpression(a, context, cancellationToken)).ToList();

        return functionCall.FunctionName.ToLowerInvariant() switch
        {
            "length" => EvaluateLength(args),
            "trim" => EvaluateTrim(args),
            "upper" => EvaluateUpper(args),
            "lower" => EvaluateLower(args),
            "now" => context.Metadata.Now,
            "items" => EvaluateItems(args, context),
            "jmespath" => EvaluateJmesPath(args),
            _ => throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.SecurityViolation,
                Expression = $"{functionCall.FunctionName}(...)",
                Reason = $"函数 '{functionCall.FunctionName}' 不在白名单中，不允许调用。"
            })
        };
    }

    private static object EvaluateLength(IReadOnlyList<object?> args)
    {
        if (args.Count != 1)
        {
            throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.TypeMismatch,
                Reason = "length() 函数需要 1 个参数。"
            });
        }

        var value = args[0];
        return value switch
        {
            string s => s.Length,
            JsonArray array => array.Count,
            IEnumerable enumerable => enumerable.Cast<object>().Count(),
            _ => throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.TypeMismatch,
                Reason = $"类型 '{value?.GetType().Name}' 不支持 length()。"
            })
        };
    }

    private static object EvaluateTrim(IReadOnlyList<object?> args)
    {
        var value = GetSingleString(args, "trim");
        return value.Trim();
    }

    private static object EvaluateUpper(IReadOnlyList<object?> args)
    {
        var value = GetSingleString(args, "upper");
        return value.ToUpperInvariant();
    }

    private static object EvaluateLower(IReadOnlyList<object?> args)
    {
        var value = GetSingleString(args, "lower");
        return value.ToLowerInvariant();
    }

    private static object? EvaluateJmesPath(IReadOnlyList<object?> args)
    {
        if (args.Count != 2)
        {
            throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.TypeMismatch,
                Reason = "jmespath() 函数需要 2 个参数（data, query）。"
            });
        }

        var data = args[0];
        var query = args[1]?.ToString()
            ?? throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.TypeMismatch,
                Reason = "jmespath() 查询表达式不能为 null。"
            });

        try
        {
            var json = ConvertToJToken(data);
            var jmespath = new DevLab.JmesPath.JmesPath();
            var result = jmespath.Transform(json.ToString(Newtonsoft.Json.Formatting.None), query);
            return ConvertFromJToken(JToken.Parse(result));
        }
        catch (Exception ex)
        {
            throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.SyntaxError,
                Expression = $"jmespath(..., \"{query}\")",
                Reason = $"JMESPath 查询失败：{ex.Message}"
            });
        }
    }

    private static JToken ConvertToJToken(object? value)
    {
        return value switch
        {
            null => JValue.CreateNull(),
            JToken jToken => jToken,
            JsonNode jsonNode => JToken.Parse(jsonNode.ToJsonString()),
            string s => JValue.CreateString(s),
            int i => new JValue(i),
            long l => new JValue(l),
            double d => new JValue(d),
            float f => new JValue(f),
            decimal d => new JValue(d),
            bool b => new JValue(b),
            _ => JValue.CreateString(value.ToString())
        };
    }

    private static object? ConvertFromJToken(JToken? token)
    {
        if (token is null or JValue { Type: JTokenType.Null })
        {
            return null;
        }

        return token.Type switch
        {
            JTokenType.Integer => token.Value<long>(),
            JTokenType.Float => token.Value<double>(),
            JTokenType.Boolean => token.Value<bool>(),
            JTokenType.String => token.Value<string>(),
            JTokenType.Array => JsonNode.Parse(token.ToString()),
            JTokenType.Object => JsonNode.Parse(token.ToString()),
            _ => token.ToString()
        };
    }

    private static string GetSingleString(IReadOnlyList<object?> args, string functionName)
    {
        if (args.Count != 1)
        {
            throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.TypeMismatch,
                Reason = $"{functionName}() 函数需要 1 个参数。"
            });
        }

        return args[0]?.ToString()
            ?? throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.TypeMismatch,
                Reason = $"{functionName}() 函数参数不能为 null。"
            });
    }

    private static DataBatch EvaluateItems(IReadOnlyList<object?> args, ExpressionContext context)
    {
        if (args.Count != 1 || args[0] is not string nodeName)
        {
            throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.TypeMismatch,
                Reason = "items() 函数需要 1 个字符串参数。"
            });
        }

        if (!context.NodeBatches.TryGetValue(nodeName, out var batch))
        {
            throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.NodeOutputNotFound,
                Expression = $"items(\"{nodeName}\")",
                Reason = $"节点 '{nodeName}' 的批次不存在。",
                AvailableFields = context.NodeBatches.Keys.ToList()
            });
        }

        return batch;
    }

    private object? EvaluateBinary(BinaryOperationNode binary, ExpressionContext context, CancellationToken cancellationToken)
    {
        var left = EvaluateExpression(binary.Left, context, cancellationToken);

        return binary.Operator switch
        {
            BinaryOperator.Add => Add(left, EvaluateExpression(binary.Right, context, cancellationToken)),
            BinaryOperator.Subtract => Subtract(left, EvaluateExpression(binary.Right, context, cancellationToken)),
            BinaryOperator.Multiply => Multiply(left, EvaluateExpression(binary.Right, context, cancellationToken)),
            BinaryOperator.Divide => Divide(left, EvaluateExpression(binary.Right, context, cancellationToken)),
            BinaryOperator.Modulo => Modulo(left, EvaluateExpression(binary.Right, context, cancellationToken)),
            BinaryOperator.Equal => Equal(left, EvaluateExpression(binary.Right, context, cancellationToken)),
            BinaryOperator.NotEqual => !Equal(left, EvaluateExpression(binary.Right, context, cancellationToken)),
            BinaryOperator.GreaterThan => Compare(left, EvaluateExpression(binary.Right, context, cancellationToken)) > 0,
            BinaryOperator.LessThan => Compare(left, EvaluateExpression(binary.Right, context, cancellationToken)) < 0,
            BinaryOperator.GreaterThanOrEqual => Compare(left, EvaluateExpression(binary.Right, context, cancellationToken)) >= 0,
            BinaryOperator.LessThanOrEqual => Compare(left, EvaluateExpression(binary.Right, context, cancellationToken)) <= 0,
            BinaryOperator.And => ToBoolean(left) && ToBoolean(EvaluateExpression(binary.Right, context, cancellationToken)),
            BinaryOperator.Or => ToBoolean(left) || ToBoolean(EvaluateExpression(binary.Right, context, cancellationToken)),
            _ => throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.SyntaxError,
                Reason = $"不支持的二元运算符：{binary.Operator}。"
            })
        };
    }

    private object? EvaluateUnary(UnaryOperationNode unary, ExpressionContext context, CancellationToken cancellationToken)
    {
        var operand = EvaluateExpression(unary.Operand, context, cancellationToken);

        return unary.Operator switch
        {
            UnaryOperator.Not => !ToBoolean(operand),
            UnaryOperator.Negate => Negate(operand),
            UnaryOperator.Plus => operand,
            _ => throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.SyntaxError,
                Reason = $"不支持的一元运算符：{unary.Operator}。"
            })
        };
    }

    private object? EvaluateTernary(TernaryNode ternary, ExpressionContext context, CancellationToken cancellationToken)
    {
        var condition = ToBoolean(EvaluateExpression(ternary.Condition, context, cancellationToken));
        return condition
            ? EvaluateExpression(ternary.TrueExpression, context, cancellationToken)
            : EvaluateExpression(ternary.FalseExpression, context, cancellationToken);
    }

    private static object Add(object? left, object? right)
    {
        if (left is string || right is string)
        {
            return $"{ConvertToString(left)}{ConvertToString(right)}";
        }

        return ApplyNumeric(left, right, (a, b) => a + b);
    }

    private static object Subtract(object? left, object? right) =>
        ApplyNumeric(left, right, (a, b) => a - b);

    private static object Multiply(object? left, object? right) =>
        ApplyNumeric(left, right, (a, b) => a * b);

    private static object Divide(object? left, object? right) =>
        ApplyNumeric(left, right, (a, b) => a / b);

    private static object Modulo(object? left, object? right) =>
        ApplyNumeric(left, right, (a, b) => a % b);

    private static object ApplyNumeric(object? left, object? right, Func<double, double, double> operation)
    {
        var leftNumber = ConvertToDouble(left);
        var rightNumber = ConvertToDouble(right);
        var result = operation(leftNumber, rightNumber);

        if (double.IsInteger(result) && left is int && right is int)
        {
            return (int)result;
        }

        return result;
    }

    private static bool Equal(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.GetType() == right.GetType())
        {
            return left.Equals(right);
        }

        return ConvertToDouble(left) == ConvertToDouble(right);
    }

    private static int Compare(object? left, object? right)
    {
        var leftNumber = ConvertToDouble(left);
        var rightNumber = ConvertToDouble(right);
        return leftNumber.CompareTo(rightNumber);
    }

    private static bool ToBoolean(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is bool b)
        {
            return b;
        }

        if (value is string s)
        {
            return !string.IsNullOrEmpty(s);
        }

        return Convert.ToDouble(value) != 0;
    }

    private static object Negate(object? value)
    {
        var number = ConvertToDouble(value);
        return number == 0 && value is int ? 0 : -number;
    }

    private static double ConvertToDouble(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value is double d)
        {
            return d;
        }

        if (value is int i)
        {
            return i;
        }

        if (value is long l)
        {
            return l;
        }

        if (value is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new ExpressionEvaluationException(new ExpressionError
        {
            Type = ExpressionErrorType.TypeMismatch,
            Reason = $"值 '{value}' 无法转换为数字。"
        });
    }

    private static string ConvertToString(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Try to parse a comparison expression from a literal segment following an expression result.
    /// e.g. value=200, text=" == 200" → evaluates 200 == 200 → true.
    /// </summary>
    private bool? TryParseComparisonFromLiteral(
        object? leftValue,
        string literalText,
        ExpressionContext context,
        IReadOnlyList<Segment> segments,
        ref int index,
        CancellationToken cancellationToken)
    {
        var trimmed = literalText.TrimStart();

        var operators = new[] { "==", "!=", ">=", "<=", ">", "<" };
        foreach (var op in operators)
        {
            if (!trimmed.StartsWith(op, StringComparison.Ordinal))
            {
                continue;
            }

            var rightText = trimmed[op.Length..].Trim();

            // If right side is empty, there might be another expression segment following
            if (string.IsNullOrEmpty(rightText) && index + 2 < segments.Count && segments[index + 2] is ExpressionSegment rightExpr)
            {
                var rightValue = EvaluateExpression(rightExpr.Expression, context, cancellationToken);
                index += 2; // skip the literal and expression segments
                return CompareValues(leftValue, rightValue, op);
            }

            // Right side is a literal value (number, string, etc.)
            if (!string.IsNullOrEmpty(rightText))
            {
                var rightValue = ParseLiteralValue(rightText);
                return CompareValues(leftValue, rightValue, op);
            }
        }

        return null;
    }

    private static object? ParseLiteralValue(string text)
    {
        if (bool.TryParse(text, out var b))
        {
            return b;
        }

        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        return text;
    }

    private static bool CompareValues(object? left, object? right, string op)
    {
        if (left is null && right is null)
        {
            return op == "==";
        }

        if (left is null || right is null)
        {
            return op == "!=";
        }

        // Try numeric comparison
        if (left is IConvertible && right is IConvertible)
        {
            try
            {
                var leftNum = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                var rightNum = Convert.ToDouble(right, CultureInfo.InvariantCulture);
                return op switch
                {
                    "==" => leftNum == rightNum,
                    "!=" => leftNum != rightNum,
                    ">=" => leftNum >= rightNum,
                    "<=" => leftNum <= rightNum,
                    ">" => leftNum > rightNum,
                    "<" => leftNum < rightNum,
                    _ => false,
                };
            }
            catch
            {
                // Fall through to string comparison
            }
        }

        var leftStr = left.ToString() ?? string.Empty;
        var rightStr = right.ToString() ?? string.Empty;

        return op switch
        {
            "==" => string.Equals(leftStr, rightStr, StringComparison.Ordinal),
            "!=" => !string.Equals(leftStr, rightStr, StringComparison.Ordinal),
            ">=" => string.Compare(leftStr, rightStr, StringComparison.Ordinal) >= 0,
            "<=" => string.Compare(leftStr, rightStr, StringComparison.Ordinal) <= 0,
            ">" => string.Compare(leftStr, rightStr, StringComparison.Ordinal) > 0,
            "<" => string.Compare(leftStr, rightStr, StringComparison.Ordinal) < 0,
            _ => false,
        };
    }

    /// <summary>
    /// 环境变量包装器，仅允许访问白名单中的变量。
    /// </summary>
    private sealed class EnvironmentWrapper
    {
        private readonly IReadOnlySet<string> _whitelist;

        public EnvironmentWrapper(IReadOnlySet<string> whitelist)
        {
            _whitelist = whitelist;
        }

        public object? GetMember(string name)
        {
            if (!_whitelist.Contains(name))
            {
                throw new ExpressionEvaluationException(new ExpressionError
                {
                    Type = ExpressionErrorType.SecurityViolation,
                    Expression = $"env.{name}",
                    Reason = $"环境变量 '{name}' 不在访问白名单中。",
                    AvailableFields = _whitelist.ToList()
                });
            }

            return Environment.GetEnvironmentVariable(name);
        }
    }

    /// <summary>
    /// 节点输出包装器，访问不存在的节点时抛出 <see cref="ExpressionErrorType.NodeOutputNotFound"/>。
    /// </summary>
    private sealed class NodeOutputsWrapper(IReadOnlyDictionary<string, DataBatch> outputs)
    {
        public IReadOnlyDictionary<string, DataBatch> Outputs { get; } = outputs;
    }
}
