using System.Runtime.CompilerServices;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Entities;
using Mapster;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流相关实体与 DTO 的 Mapster 映射配置。
/// 通过模块初始化器注册到全局 <see cref="TypeAdapterConfig.GlobalSettings"/>，
/// 其余字段由 Mapster 按名称约定自动映射；此处仅集中维护需要特殊处理的映射。
/// </summary>
public static class WorkflowMapper
{
    /// <summary>
    /// 模块加载时注册全局映射配置（仅执行一次）。
    /// </summary>
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Register()
    {
#pragma warning disable CS8603
        var config = TypeAdapterConfig.GlobalSettings;

        // 节点定义：双向按名称映射；实体 Id 为字符串，与 DTO 一致。
        config.ForType<NodeDefinitionDto, NodeDefinition>();
        config.ForType<NodeDefinition, NodeDefinitionDto>();

        // 工作流：节点/连接集合按上述元素级配置映射。
        config.ForType<Workflow, WorkflowDto>();

        // 触发器配置：双向按名称映射（实体较 DTO 多的字段本就不参与映射）。
        config.ForType<TriggerSettings, TriggerSettingsDto>();
        config.ForType<TriggerSettingsDto, TriggerSettings>();

        // 执行记录：节点记录含自定义序列化，需忽略后单独处理；状态枚举由 Mapster 自动转为字符串。
        config.ForType<ExecutionRecord, ExecutionSummaryDto>();
        var executionToDto = config.ForType<ExecutionRecord, ExecutionDto>()!;
        executionToDto.Ignore(e => e.NodeRecords);

        // 连接：实体主键 Id 为 Guid，DTO Id 为字符串，需显式转换；
        // 反向（DTO→实体）忽略 Id，由实体基类生成新主键。
        var connectionToDto = config.ForType<Connection, ConnectionDto>()!;
        connectionToDto.Map(c => c.Id, src => src.Id.ToString());
        var connectionToEntity = config.ForType<ConnectionDto, Connection>()!;
        connectionToEntity.Ignore(c => c.Id);

        // 触发器：DTO 含 UpdatedAt 字段，但原手工映射未赋值，按既有行为忽略以保持一致。
        var triggerToDto = config.ForType<Trigger, TriggerDto>()!;
        triggerToDto.Ignore(t => t.UpdatedAt);
#pragma warning restore CS8603
    }
}
