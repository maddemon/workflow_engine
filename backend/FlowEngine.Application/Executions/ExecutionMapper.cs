using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Core;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Mapster;

namespace FlowEngine.Application.Executions;

/// <summary>
/// 执行记录到 DTO 的共享映射逻辑，供 ExecutionService 和 WorkflowDryRunService 复用。
/// </summary>
internal static class ExecutionMapper
{
    /// <summary>
    /// 将 <see cref="ExecutionRecord"/> 映射为 <see cref="ExecutionDto"/>，
    /// 节点记录含自定义序列化，单独处理。
    /// </summary>
    public static ExecutionDto MapToDto(ExecutionRecord record)
    {
        var dto = record.Adapt<ExecutionDto>();
        return dto with { NodeRecords = record.NodeRecords.Select(MapToNodeRecord).ToList() };
    }

    /// <summary>
    /// 将 <see cref="NodeExecutionRecord"/> 映射为 <see cref="NodeExecutionRecordDto"/>。
    /// </summary>
    public static NodeExecutionRecordDto MapToNodeRecord(NodeExecutionRecord node)
    {
        return new NodeExecutionRecordDto
        {
            Id = node.Id,
            NodeDefinitionId = node.NodeDefinitionId,
            RunIndex = node.RunIndex,
            Status = node.Output.Success ? "Completed" : "Failed",
            StartedAt = node.StartedAt ?? default,
            CompletedAt = node.CompletedAt,
            Inputs = SerializeInputs(node.Inputs),
            Output = node.Output is null ? null : JsonSerializer.SerializeToNode(node.Output, JsonDefaults.Options),
            RawParameters = SerializeToDictionary(node.RawParameters),
            ResolvedParameters = SerializeToDictionary(node.ResolvedParameters)
        };
    }

    /// <summary>
    /// 序列化节点输入为 DTO 可用的字典格式。
    /// </summary>
    public static Dictionary<string, object>? SerializeInputs(IReadOnlyDictionary<string, DataBatch>? inputs)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, object>(inputs.Count);
        foreach (var (key, value) in inputs)
        {
            result[key] = JsonSerializer.SerializeToNode(value, JsonDefaults.Options) ?? string.Empty;
        }

        return result;
    }

    /// <summary>
    /// 序列化参数字典为 DTO 可用的格式。
    /// </summary>
    public static Dictionary<string, object>? SerializeToDictionary<TKey>(IReadOnlyDictionary<TKey, object>? dict)
        where TKey : notnull
    {
        if (dict is null || dict.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, object>(dict.Count);
        foreach (var (key, value) in dict)
        {
            result[key.ToString()!] = value is string or int or long or double or float or decimal or bool or DateTime
                ? value
                : JsonSerializer.SerializeToNode(value, JsonDefaults.Options) ?? string.Empty;
        }

        return result;
    }
}
