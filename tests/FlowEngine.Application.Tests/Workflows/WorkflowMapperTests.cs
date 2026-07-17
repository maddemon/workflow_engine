using FlowEngine.Application.Dtos;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Mapster;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// 验证 <see cref="WorkflowMapper"/> 注册的 Mapster 映射配置（取代原手工映射）正确映射关键字段，
/// 包括使用自定义配置的连接主键（Guid → string）与执行记录（忽略 NodeRecords）。
/// </summary>
public sealed class WorkflowMapperTests
{
    public WorkflowMapperTests()
    {
        // 全局映射配置由 FlowEngine.Application 程序集的 [ModuleInitializer] 在加载时自动注册到
        // TypeAdapterConfig.GlobalSettings，无需在测试中重复调用 WorkflowMapper.Register()（重复调用会抛异常）。
    }

    [Fact]
    public void Workflow_To_WorkflowDto_MapsKeyFields()
    {
        var nodeId = "n1";
        var connectionId = Guid.NewGuid();
        var workflow = new Workflow
        {
            Id = Guid.CreateVersion7(),
            ProjectId = Guid.CreateVersion7(),
            Name = "My Workflow",
            CreatedBy = "tester",
            IsActive = true,
            Nodes =
            [
                new NodeDefinition
                {
                    Id = nodeId,
                    TypeName = "httpRequest",
                    Name = "Fetch",
                    Parameters = new() { ["url"] = "https://api.example.com" },
                },
            ],
            Connections =
            [
                new Connection
                {
                    Id = connectionId,
                    SourceNodeId = nodeId,
                    SourcePortName = "output",
                    TargetNodeId = "n2",
                    TargetPortName = "input",
                },
            ],
        };

        var dto = workflow.Adapt<WorkflowDto>();

        Assert.Equal(workflow.Id, dto.Id);
        Assert.Equal(workflow.ProjectId, dto.ProjectId);
        Assert.Equal("My Workflow", dto.Name);
        Assert.True(dto.IsActive);
        Assert.Single(dto.Nodes);
        Assert.Equal("Fetch", dto.Nodes[0].Name);
        Assert.Equal("httpRequest", dto.Nodes[0].TypeName);
        Assert.Single(dto.Connections);
        // 自定义映射：Connection.Id (Guid) -> ConnectionDto.Id (string)
        Assert.Equal(connectionId.ToString(), dto.Connections[0].Id);
        Assert.Equal(nodeId, dto.Connections[0].SourceNodeId);
    }

    [Fact]
    public void ExecutionRecord_To_ExecutionDto_MapsStatusAndIgnoresNodeRecords()
    {
        var record = new ExecutionRecord
        {
            Id = Guid.CreateVersion7(),
            WorkflowDefinitionId = Guid.CreateVersion7(),
            Status = ExecutionStatus.Completed,
        };

        var dto = record.Adapt<ExecutionDto>();

        // 状态枚举由 Mapster 自动映射为目标字符串
        Assert.Equal("Completed", dto.Status);
        // 自定义映射：ExecutionDto.NodeRecords 被显式忽略，不应被映射（保持默认空集合）
        Assert.Empty(dto.NodeRecords);
    }

    [Fact]
    public void GlobalTypeAdapterConfig_IsRegistered()
    {
        // 全局配置实例已由 [ModuleInitializer] 注册，且实际参与映射（Workflow -> WorkflowDto 可用并正确）。
        var config = TypeAdapterConfig.GlobalSettings;
        Assert.NotNull(config);

        var mapped = new Workflow
        {
            Id = Guid.CreateVersion7(),
            Name = "CfgCheck",
        }.Adapt<WorkflowDto>();

        Assert.Equal("CfgCheck", mapped.Name);
    }

    [Fact]
    public void ConnectionDto_To_Connection_IgnoresId()
    {
        var dto = new ConnectionDto
        {
            Id = "some-string-id",
            SourceNodeId = "n1",
            SourcePortName = "output",
            TargetNodeId = "n2",
            TargetPortName = "input",
        };

        var entity = dto.Adapt<Connection>();

        // 反向映射忽略 DTO 传入的字符串 Id，由实体基类生成新 Guid 主键。
        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.NotEqual("some-string-id", entity.Id.ToString());
        Assert.Equal("n1", entity.SourceNodeId);
        Assert.Equal("n2", entity.TargetNodeId);
    }

    [Fact]
    public void NodeDefinitionDto_To_NodeDefinition_MapsDisabled()
    {
        var dto = new NodeDefinitionDto
        {
            Id = "n1",
            TypeName = "httpRequest",
            Name = "Fetch",
            Disabled = true,
        };

        var entity = dto.Adapt<NodeDefinition>();

        Assert.Equal("n1", entity.Id);
        Assert.True(entity.Disabled);
    }

    [Fact]
    public void NodeDefinition_To_NodeDefinitionDto_MapsDisabled()
    {
        var entity = new NodeDefinition
        {
            Id = "n1",
            TypeName = "httpRequest",
            Name = "Fetch",
            Disabled = true,
        };

        var dto = entity.Adapt<NodeDefinitionDto>();

        Assert.True(dto.Disabled);
    }
}
