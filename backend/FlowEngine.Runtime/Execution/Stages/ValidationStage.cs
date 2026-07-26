using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Metadata;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Execution.Pipeline;
using FlowEngine.Runtime.Registry;

namespace FlowEngine.Runtime.Execution.Stages;

/// <summary>
/// 校验阶段：在初始化之后、求值/执行之前，对节点的原始声明参数做声明式 [Required] 校验。
/// 逻辑：反射节点类型上标记 <see cref="RequiredAttribute"/> 的公共实例属性，按 camelCase 参数名
/// 在「声明参数 ∪ 描述符默认值」的合并结果中查找；缺失或空白（string 空白 / Script 空源）记为校验错误。
/// 校验失败则构造 <c>ValidationFailed</c> 失败结果并短路至末端持久化阶段（不调用 next），
/// 由 <see cref="PersistenceStage"/> 负责落库与路由，行为等价于真实节点失败。
/// </summary>
public sealed class ValidationStage(INodeRegistry nodeRegistry) : IExecutionStage
{
    /// <summary>执行声明式参数校验。校验失败设置 <see cref="NodePipelineContext.Result"/> 并短路；否则驱动后续阶段。</summary>
    /// <param name="context">管线上下文（由 <see cref="InitializeStage"/> 填充 NodeDefinition / NodeType）。</param>
    /// <param name="next">后续阶段驱动委托。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct)
    {
        var node = context.NodeDefinition;
        var nodeType = context.NodeType;

        // 节点缺失（初始化阶段未填充）时不校验，交由后续阶段处理。
        if (node is null || nodeType is null)
        {
            await next();
            return;
        }

        var descriptor = nodeRegistry.GetDescriptor(nodeType.TypeName);

        // 合并声明参数与描述符默认值，得到每个参数的最终原始值（缺失值的判定依据）。
        var merged = MergeParameters(node, descriptor);

        // 反射节点类型上标记的 [Required] 属性。
        var requiredProps = nodeType.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<RequiredAttribute>() is not null)
            .ToList();

        foreach (var prop in requiredProps)
        {
            var paramName = ParameterDiscoverer.ToCamelCase(prop.Name);
            var present = merged.TryGetValue(paramName, out var rawValue) && rawValue is not null;

            if (!present)
            {
                context.ValidationErrors.Add(new ValidationError(
                    paramName, "Required", $"参数 '{paramName}' 为必填项，但未提供。"));
                continue;
            }

            // 存在但为空的检查。
            if (rawValue is string strVal && string.IsNullOrWhiteSpace(strVal))
            {
                context.ValidationErrors.Add(new ValidationError(
                    paramName, "Required", $"参数 '{paramName}' 不能为空白字符串。"));
            }
            else if (rawValue is Script scriptVal && string.IsNullOrWhiteSpace(scriptVal.Source))
            {
                context.ValidationErrors.Add(new ValidationError(
                    paramName, "Required", $"参数 '{paramName}' 脚本内容不能为空。"));
            }
            // 其他类型：存在即视为通过（值类型/复杂对象不在此做空值判定）。
        }

        if (context.ValidationErrors.Count > 0)
        {
            var message = "声明式参数校验失败：" +
                string.Join("; ", context.ValidationErrors.Select(e => e.Message));
            context.Result = new NodeExecutionResult
            {
                Success = false,
                Error = new NodeError
                {
                    Code = "ValidationFailed",
                    Message = message,
                    NodeDefinitionId = node.Id
                }
            };

            // 短路：不调用 next，由管线驱动器跳转至末端持久化阶段。
            return;
        }

        await next();
    }

    /// <summary>
    /// 合并节点声明参数与描述符默认值（复制自 <see cref="NodeExecutionContextFactory"/> 的合并逻辑，
    /// 供校验阶段独立判定参数「是否提供」）。
    /// </summary>
    private static Dictionary<string, object> MergeParameters(NodeDefinition nodeDefinition, NodeTypeDescriptor descriptor)
    {
        var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in descriptor.Parameters)
        {
            if (nodeDefinition.Parameters.TryGetValue(parameter.Name, out var value))
            {
                merged[parameter.Name] = value;
            }
            else if (parameter.DefaultValue is not null)
            {
                merged[parameter.Name] = parameter.DefaultValue;
            }
        }

        foreach (var (key, value) in nodeDefinition.Parameters)
        {
            if (!merged.ContainsKey(key))
            {
                merged[key] = value;
            }
        }

        return merged;
    }
}
