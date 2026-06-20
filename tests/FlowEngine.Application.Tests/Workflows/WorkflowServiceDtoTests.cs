using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// 工作流服务测试 —— 覆盖 DTO 转换和字符串 ID 映射。
/// </summary>
public class WorkflowServiceDtoTests
{
    [Fact]
    public void CreateWorkflowDto_Accepts_String_NodeIds()
    {
        var dto = new CreateWorkflowDto
        {
            Name = "Test Workflow",
            CreatedBy = "test",
            Nodes =
            [
                new NodeInstanceDto
                {
                    Id = "node_a",
                    TypeName = "httpRequest",
                    Name = "HTTP",
                    Parameters = new() { ["url"] = "https://example.com" },
                    Ports = [],
                    PositionX = 100,
                    PositionY = 200,
                    IsEntry = true,
                    ErrorStrategy = ErrorStrategy.Terminate,
                }
            ],
            Connections = [],
        };

        Assert.Equal("node_a", dto.Nodes[0].Id);
        Assert.Equal("httpRequest", dto.Nodes[0].TypeName);
    }

    [Fact]
    public void CreateWorkflowDto_Accepts_String_ConnectionIds()
    {
        var dto = new CreateWorkflowDto
        {
            Name = "Test",
            CreatedBy = "test",
            Nodes =
            [
                new NodeInstanceDto { Id = "a", TypeName = "httpRequest", Name = "A", Ports = [], ErrorStrategy = ErrorStrategy.Terminate },
                new NodeInstanceDto { Id = "b", TypeName = "if", Name = "B", Ports = [], ErrorStrategy = ErrorStrategy.Terminate },
            ],
            Connections =
            [
                new ConnectionDto
                {
                    Id = "e_a_b",
                    SourceNodeId = "a",
                    SourcePortName = "output",
                    TargetNodeId = "b",
                    TargetPortName = "input",
                }
            ],
        };

        Assert.Equal("e_a_b", dto.Connections[0].Id);
        Assert.Equal("a", dto.Connections[0].SourceNodeId);
        Assert.Equal("b", dto.Connections[0].TargetNodeId);
    }

    [Fact]
    public void NodeInstanceDto_Accepts_All_ErrorStrategies()
    {
        foreach (var strategy in Enum.GetValues<ErrorStrategy>())
        {
            var dto = new NodeInstanceDto
            {
                Id = "test",
                TypeName = "script",
                Name = "Test",
                Ports = [],
                ErrorStrategy = strategy,
            };

            Assert.Equal(strategy, dto.ErrorStrategy);
        }
    }

    [Fact]
    public void UpdateWorkflowDto_Supports_StyleSettings()
    {
        var dto = new UpdateWorkflowDto
        {
            Name = "Updated",
            IsActive = true,
            StyleSettings = new WorkflowStyleSettings { LayoutDirection = "horizontal" },
            Nodes = [],
            Connections = [],
        };

        Assert.Equal("horizontal", dto.StyleSettings?.LayoutDirection);
    }

    [Fact]
    public void WorkflowDto_Returns_Nodes_As_Dto_Type()
    {
        var dto = new WorkflowDto
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Nodes =
            [
                new NodeInstanceDto { Id = "n1", TypeName = "httpRequest", Name = "HTTP", Ports = [], ErrorStrategy = ErrorStrategy.Terminate },
            ],
            Connections =
            [
                new ConnectionDto { Id = "c1", SourceNodeId = "n1", SourcePortName = "out", TargetNodeId = "n2", TargetPortName = "in" },
            ],
        };

        Assert.Single(dto.Nodes);
        Assert.Single(dto.Connections);
        Assert.Equal("n1", dto.Nodes[0].Id);
        Assert.Equal("n1", dto.Connections[0].SourceNodeId);
    }
}
