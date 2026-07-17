using FlowEngine.Application.Dtos;
using FlowEngine.Application.Validators;
using Xunit;

namespace FlowEngine.Application.Tests.Validators;

/// <summary>
/// FluentValidation 校验器行为测试（任务 1.2）。
/// </summary>
public sealed class ValidatorTests
{
    [Fact]
    public void AssignRoleRequest_EmptyRole_Fails()
    {
        var validator = new AssignRoleRequestValidator();
        var result = validator.Validate(new AssignRoleRequest { Role = "" });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void AssignRoleRequest_InvalidRole_Fails()
    {
        var validator = new AssignRoleRequestValidator();
        var result = validator.Validate(new AssignRoleRequest { Role = "SuperUser" });
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Editor")]
    [InlineData("Viewer")]
    [InlineData("admin")]
    public void AssignRoleRequest_ValidRole_Passes(string role)
    {
        var validator = new AssignRoleRequestValidator();
        var result = validator.Validate(new AssignRoleRequest { Role = role });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateProjectDto_EmptyName_Fails()
    {
        var validator = new CreateProjectDtoValidator();
        var result = validator.Validate(new CreateProjectDto { Name = "" });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateProjectDto_Valid_Passes()
    {
        var validator = new CreateProjectDtoValidator();
        var result = validator.Validate(new CreateProjectDto { Name = "P", Description = "d" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateCredentialDto_EmptyName_Fails()
    {
        var validator = new CreateCredentialDtoValidator();
        var result = validator.Validate(new CreateCredentialDto { Name = "", Type = "t", Fields = new() { ["k"] = "v" } });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateCredentialDto_EmptyType_Fails()
    {
        var validator = new CreateCredentialDtoValidator();
        var result = validator.Validate(new CreateCredentialDto { Name = "n", Type = "", Fields = new() { ["k"] = "v" } });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateCredentialDto_EmptyFields_Fails()
    {
        var validator = new CreateCredentialDtoValidator();
        var result = validator.Validate(new CreateCredentialDto { Name = "n", Type = "t", Fields = [] });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateCredentialDto_Valid_Passes()
    {
        var validator = new CreateCredentialDtoValidator();
        var result = validator.Validate(new CreateCredentialDto { Name = "n", Type = "t", Fields = new() { ["k"] = "v" } });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateWorkflowDto_EmptyName_Fails()
    {
        var validator = new CreateWorkflowDtoValidator();
        var result = validator.Validate(new CreateWorkflowDto { Name = "" });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateWorkflowDto_Valid_Passes()
    {
        var validator = new CreateWorkflowDtoValidator();
        var result = validator.Validate(new CreateWorkflowDto { Name = "w" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateWorkflowDto_EmptyName_Fails()
    {
        var validator = new UpdateWorkflowDtoValidator();
        var result = validator.Validate(new UpdateWorkflowDto { Name = "" });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void UpdateWorkflowDto_Valid_Passes()
    {
        var validator = new UpdateWorkflowDtoValidator();
        var result = validator.Validate(new UpdateWorkflowDto { Name = "w" });
        Assert.True(result.IsValid);
    }
}
