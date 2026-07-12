using FlowEngine.Application.Dtos;
using FlowEngine.Core.Entities;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流实体与 DTO 之间的双向映射工具，消除 WorkflowService/ImportService/ExportService 中的重复映射代码。
/// </summary>
public static class WorkflowMapper
{
    /// <summary>
    /// 将 NodeDefinitionDto 转换为 NodeDefinition 实体。
    /// </summary>
    public static NodeDefinition ToEntity(NodeDefinitionDto dto)
    {
        return new NodeDefinition
        {
            Id = dto.Id,
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
    }

    /// <summary>
    /// 将 NodeDefinition 实体转换为 NodeDefinitionDto。
    /// </summary>
    public static NodeDefinitionDto ToDto(NodeDefinition entity, string? id = null)
    {
        return new NodeDefinitionDto
        {
            Id = id ?? entity.Id,
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
    public static Connection ToEntity(ConnectionDto dto)
    {
        return new Connection
        {
            SourceNodeId = dto.SourceNodeId,
            SourcePortName = dto.SourcePortName,
            TargetNodeId = dto.TargetNodeId,
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
            SourceNodeId = sourceNodeId ?? entity.SourceNodeId,
            SourcePortName = entity.SourcePortName,
            TargetNodeId = targetNodeId ?? entity.TargetNodeId,
            TargetPortName = entity.TargetPortName,
            Condition = entity.Condition,
        };
    }
}
