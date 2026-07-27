using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Tests;

/// <summary>
/// 实体属性往返测试：构造后验证关键属性的 get/set 往返，
/// 并明确 NodeDefinition.Id(string) 与 Entity.Id(Guid) 的类型差异。
/// </summary>
public class EntitiesPropertyTests
{
    [Fact]
    public void Entity_Base_Assigns_UuidV7_Id_And_Defaults()
    {
        var before = DateTime.UtcNow;
        var project = new Project(); // 通过具体子类实例化抽象 Entity
        var after = DateTime.UtcNow;

        // Id 由构造器以 UUIDv7 自动生成，非空
        Assert.NotEqual(Guid.Empty, project.Id);

        // 时间戳与删除标记默认值
        Assert.InRange(project.CreatedAt, before, after);
        Assert.Null(project.UpdatedAt);
        Assert.False(project.Deleted);
    }

    [Fact]
    public void Entity_Id_CanBeOverridden()
    {
        var id = Guid.CreateVersion7();
        var project = new Project { Id = id };

        Assert.Equal(id, project.Id);
    }

    [Fact]
    public void Project_Properties_RoundTrip()
    {
        var id = Guid.CreateVersion7();
        var createdBy = Guid.NewGuid().ToString();
        var project = new Project
        {
            Id = id,
            Name = "My Project",
            Description = "项目描述",
            CreatedBy = createdBy
        };

        Assert.Equal(id, project.Id);
        Assert.Equal("My Project", project.Name);
        Assert.Equal("项目描述", project.Description);
        Assert.Equal(createdBy, project.CreatedBy);

        // 可选字段可清空，必填字段可重设
        project.Description = null;
        Assert.Null(project.Description);

        project.Name = "重命名";
        Assert.Equal("重命名", project.Name);
    }

    [Fact]
    public void Project_Name_Defaults_To_Empty()
    {
        var project = new Project();

        Assert.Equal(string.Empty, project.Name);
        Assert.Null(project.Description);
        Assert.Equal(string.Empty, project.CreatedBy);
    }

    [Fact]
    public void Workflow_KeyProperties_RoundTrip()
    {
        var projectId = Guid.NewGuid();
        var workflow = new Workflow
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            Name = "Flow",
            Version = 3,
            CreatedBy = "author",
            IsActive = true,
            Source = WorkflowSource.Ai,
            DraftStatus = DraftStatus.Pending,
            RejectionReason = "原因"
        };

        Assert.Equal(projectId, workflow.ProjectId);
        Assert.Equal("Flow", workflow.Name);
        Assert.Equal(3, workflow.Version);
        Assert.Equal("author", workflow.CreatedBy);
        Assert.True(workflow.IsActive);
        Assert.Equal(WorkflowSource.Ai, workflow.Source);
        Assert.Equal(DraftStatus.Pending, workflow.DraftStatus);
        Assert.Equal("原因", workflow.RejectionReason);
    }

    [Fact]
    public void Workflow_NullableFields_Default_To_Null_Or_Zero()
    {
        var workflow = new Workflow { Name = "x" };

        Assert.Null(workflow.ProjectId);
        Assert.Equal(0, workflow.Version);
        Assert.False(workflow.IsActive);
        Assert.Equal(WorkflowSource.Human, workflow.Source); // 枚举默认 0=Human
        Assert.Null(workflow.DraftStatus);
        Assert.Null(workflow.RejectionReason);
        Assert.Empty(workflow.Nodes);
        Assert.Empty(workflow.Connections);
        Assert.Empty(workflow.Diff);
        Assert.Null(workflow.StyleSettings);
    }

    [Fact]
    public void NodeDefinition_Id_IsString_And_RoundTrips()
    {
        var node = new NodeDefinition
        {
            Id = "fetch",
            TypeName = "httpRequest",
            Name = "Fetch Data"
        };
        node.Parameters["url"] = "https://example.com";
        node.Parameters["retries"] = 3;

        // NodeDefinition.Id 是 string（AI-native 自然名称），非 Guid
        Assert.Equal("fetch", node.Id);
        Assert.Equal("httpRequest", node.TypeName);
        Assert.Equal("Fetch Data", node.Name);
        Assert.Equal(2, node.Parameters.Count);
        Assert.Equal("https://example.com", node.Parameters["url"]);

        // 重设与清空
        node.Id = "parse";
        node.Parameters.Clear();
        Assert.Equal("parse", node.Id);
        Assert.Empty(node.Parameters);
    }

    [Fact]
    public void NodeDefinition_Id_DiffersFromEntityId_Type()
    {
        // Entity.Id 为 Guid；NodeDefinition.Id 为 string —— 明确类型差异
        var node = new NodeDefinition { Id = "n1" };
        var project = new Project();

        Assert.IsType<string>(node.Id);
        Assert.IsType<Guid>(project.Id);
    }

    [Fact]
    public void NodeDefinition_Defaults_To_Empty_Collections()
    {
        var node = new NodeDefinition();

        Assert.Equal(string.Empty, node.Id);
        Assert.Equal(string.Empty, node.TypeName);
        Assert.Equal(string.Empty, node.Name);
        Assert.Empty(node.Parameters);
        Assert.Empty(node.Ports);
        Assert.Null(node.PositionX);
        Assert.Null(node.PositionY);
        Assert.False(node.IsEntry);
        Assert.False(node.Disabled);
        Assert.Null(node.RetryPolicy);
        Assert.Equal(ErrorStrategy.Terminate, node.ErrorStrategy); // enum 默认
        Assert.Null(node.Timeout);
    }
}
