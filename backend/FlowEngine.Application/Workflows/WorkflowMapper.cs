using FlowEngine.Application.Dtos;
using FlowEngine.Core.Entities;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流实体与 DTO 之间的双向映射工具，消除 WorkflowService/ImportService/ExportService 中的重复映射代码。
/// </summary>
public static class WorkflowMapper
{
    /// <summary>
    /// 将 NodeDefinitionDto 转换为 NodeDefinition 实体，生成新 Guid 并记录 ID 映射。
    /// </summary>
    public static NodeDefinition ToEntity(NodeDefinitionDto dto, Dictionary<string, Guid> nodeIdMap)
    {
        var node = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = dto.TypeName,
            Name = dto.Name,
            Parameters = dto.Parameters,
            Ports = dto.Ports,
            PositionX = dto.PositionX,
            PositionY = dto.PositionY,
            IsEntry = dto.IsEntry,
            RetryPolicy = dto.RetryPolicy,
            ErrorStrategy = dto.ErrorStrategy,
            Timeout = dto.Timeout,
        };

        if (!string.IsNullOrEmpty(dto.Id))
        {
            nodeIdMap[dto.Id] = node.Id;
        }

        return node;
    }

    /// <summary>
    /// 将 NodeDefinition 实体转换为 NodeDefinitionDto。
    /// </summary>
    public static NodeDefinitionDto ToDto(NodeDefinition entity, string? id = null)
    {
        return new NodeDefinitionDto
        {
            Id = id ?? entity.Id.ToString(),
            TypeName = entity.TypeName,
            Name = entity.Name,
            Parameters = entity.Parameters,
            Ports = entity.Ports,
            PositionX = entity.PositionX,
            PositionY = entity.PositionY,
            IsEntry = entity.IsEntry,
            RetryPolicy = entity.RetryPolicy,
            ErrorStrategy = entity.ErrorStrategy,
            Timeout = entity.Timeout,
        };
    }

    /// <summary>
    /// 将 ConnectionDto 转换为 Connection 实体。
    /// </summary>
    public static Connection ToEntity(ConnectionDto dto, Dictionary<string, Guid> nodeIdMap)
    {
        var sourceGuid = nodeIdMap.TryGetValue(dto.SourceNodeId, out var s) ? s : Guid.Empty;
        var targetGuid = nodeIdMap.TryGetValue(dto.TargetNodeId, out var t) ? t : Guid.Empty;

        return new Connection
        {
            Id = Guid.NewGuid(),
            SourceNodeId = sourceGuid,
            SourcePortName = dto.SourcePortName,
            TargetNodeId = targetGuid,
            TargetPortName = dto.TargetPortName,
            Condition = dto.Condition,
        };
    }

    /// <summary>
    /// 将 Connection 实体转换为 ConnectionDto。
    /// </summary>
    public static ConnectionDto ToDto(Connection entity, string? id = null, string? sourceNodeId = null, string? targetNodeId = null)
    {
        return new ConnectionDto
        {
            Id = id ?? entity.Id.ToString(),
            SourceNodeId = sourceNodeId ?? entity.SourceNodeId.ToString(),
            SourcePortName = entity.SourcePortName,
            TargetNodeId = targetNodeId ?? entity.TargetNodeId.ToString(),
            TargetPortName = entity.TargetPortName,
            Condition = entity.Condition,
        };
    }
}
