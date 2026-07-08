using FlowEngine.Application.Authorization;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Authorization;

public sealed class ResourceAuthorizationServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly ResourceAuthorizationService _sut;

    public ResourceAuthorizationServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        var authService = new AuthorizationService();
        _sut = new ResourceAuthorizationService(_dbContext, authService);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CanAccessWorkflowAsync_Admin_AlwaysAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Admin", ct);

        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Read, ct));
        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Write, ct));
        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Delete, ct));
        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Execute, ct));
    }

    [Fact]
    public async Task CanAccessWorkflowAsync_Editor_AllowedReadWriteExecute()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Editor", ct);
        var projectId = await SeedProjectAsync(userId, ct);
        var workflowId = await SeedWorkflowAsync(projectId, ct);

        Assert.True(await _sut.CanAccessWorkflowAsync(userId, workflowId, Operation.Read, ct));
        Assert.True(await _sut.CanAccessWorkflowAsync(userId, workflowId, Operation.Write, ct));
        Assert.True(await _sut.CanAccessWorkflowAsync(userId, workflowId, Operation.Execute, ct));
        Assert.False(await _sut.CanAccessWorkflowAsync(userId, workflowId, Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessWorkflowAsync_Viewer_OnlyReadAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Viewer", ct);
        var projectId = await SeedProjectAsync(userId, ct);
        var workflowId = await SeedWorkflowAsync(projectId, ct);

        Assert.True(await _sut.CanAccessWorkflowAsync(userId, workflowId, Operation.Read, ct));
        Assert.False(await _sut.CanAccessWorkflowAsync(userId, workflowId, Operation.Write, ct));
        Assert.False(await _sut.CanAccessWorkflowAsync(userId, workflowId, Operation.Execute, ct));
        Assert.False(await _sut.CanAccessWorkflowAsync(userId, workflowId, Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessCredentialAsync_Admin_AlwaysAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Admin", ct);

        Assert.True(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Read, ct));
        Assert.True(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Write, ct));
        Assert.True(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessCredentialAsync_Editor_AllowedReadWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Editor", ct);
        var projectId = await SeedProjectAsync(userId, ct);
        var credentialId = await SeedCredentialAsync(projectId, ct);

        Assert.True(await _sut.CanAccessCredentialAsync(userId, credentialId, Operation.Read, ct));
        Assert.True(await _sut.CanAccessCredentialAsync(userId, credentialId, Operation.Write, ct));
        Assert.False(await _sut.CanAccessCredentialAsync(userId, credentialId, Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessCredentialAsync_Viewer_OnlyReadAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Viewer", ct);
        var projectId = await SeedProjectAsync(userId, ct);
        var credentialId = await SeedCredentialAsync(projectId, ct);

        Assert.True(await _sut.CanAccessCredentialAsync(userId, credentialId, Operation.Read, ct));
        Assert.False(await _sut.CanAccessCredentialAsync(userId, credentialId, Operation.Write, ct));
        Assert.False(await _sut.CanAccessCredentialAsync(userId, credentialId, Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessExecutionAsync_Editor_AllowedReadExecute()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Editor", ct);
        var projectId = await SeedProjectAsync(userId, ct);
        var executionId = await SeedExecutionAsync(projectId, ct);

        Assert.True(await _sut.CanAccessExecutionAsync(userId, executionId, Operation.Read, ct));
        Assert.True(await _sut.CanAccessExecutionAsync(userId, executionId, Operation.Execute, ct));
        Assert.False(await _sut.CanAccessExecutionAsync(userId, executionId, Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessTriggerAsync_Editor_AllowedReadWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Editor", ct);
        var projectId = await SeedProjectAsync(userId, ct);
        var triggerId = await SeedTriggerAsync(projectId, ct);

        Assert.True(await _sut.CanAccessTriggerAsync(userId, triggerId, Operation.Read, ct));
        Assert.True(await _sut.CanAccessTriggerAsync(userId, triggerId, Operation.Write, ct));
        Assert.False(await _sut.CanAccessTriggerAsync(userId, triggerId, Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessWorkflowAsync_NoRoles_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "noroles@test.com",
            UserName = "noroles",
            PasswordHash = "hash",
            IsActive = true,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(ct);

        Assert.False(await _sut.CanAccessWorkflowAsync(user.Id, Guid.NewGuid(), Operation.Read, ct));
    }

    [Fact]
    public async Task CanAccessWorkflowAsync_OtherUsersProject_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = await SeedUserWithRoleAsync("Editor", ct);
        var otherUserId = await SeedUserWithRoleAsync("Editor", ct);
        var projectId = await SeedProjectAsync(ownerId, ct);
        var workflowId = await SeedWorkflowAsync(projectId, ct);

        Assert.False(await _sut.CanAccessWorkflowAsync(otherUserId, workflowId, Operation.Read, ct));
    }

    [Fact]
    public async Task CanAccessResourcesAsync_NullProjectId_NonAdmin_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Editor", ct);

        var workflowId = await SeedWorkflowAsync(null, ct);
        var credentialId = await SeedCredentialAsync(null, ct);
        var executionId = await SeedExecutionAsync(null, ct);
        var triggerId = await SeedTriggerAsync(null, ct);

        Assert.False(await _sut.CanAccessWorkflowAsync(userId, workflowId, Operation.Read, ct));
        Assert.False(await _sut.CanAccessCredentialAsync(userId, credentialId, Operation.Read, ct));
        Assert.False(await _sut.CanAccessExecutionAsync(userId, executionId, Operation.Read, ct));
        Assert.False(await _sut.CanAccessTriggerAsync(userId, triggerId, Operation.Read, ct));
    }

    [Fact]
    public async Task CanAccessProjectAsync_ViewerOwner_ReadOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Viewer", ct);
        var projectId = await SeedProjectAsync(userId, ct);

        Assert.True(await _sut.CanAccessProjectAsync(userId, projectId, Operation.Read, ct));
        Assert.False(await _sut.CanAccessProjectAsync(userId, projectId, Operation.Write, ct));
    }

    [Fact]
    public async Task CanAccessProjectAsync_EditorOwner_ReadWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Editor", ct);
        var projectId = await SeedProjectAsync(userId, ct);

        Assert.True(await _sut.CanAccessProjectAsync(userId, projectId, Operation.Read, ct));
        Assert.True(await _sut.CanAccessProjectAsync(userId, projectId, Operation.Write, ct));
    }

    [Fact]
    public async Task CanAccessProjectAsync_OtherUser_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = await SeedUserWithRoleAsync("Editor", ct);
        var otherUserId = await SeedUserWithRoleAsync("Editor", ct);
        var projectId = await SeedProjectAsync(ownerId, ct);

        Assert.False(await _sut.CanAccessProjectAsync(otherUserId, projectId, Operation.Read, ct));
    }

    [Fact]
    public void ShouldMaskCredentialValues_Viewer_ReturnsTrue()
    {
        Assert.True(_sut.ShouldMaskCredentialValues(["Viewer"]));
    }

    [Fact]
    public void ShouldMaskCredentialValues_Admin_ReturnsFalse()
    {
        Assert.False(_sut.ShouldMaskCredentialValues(["Admin"]));
    }

    [Fact]
    public void ShouldMaskCredentialValues_Editor_ReturnsFalse()
    {
        Assert.False(_sut.ShouldMaskCredentialValues(["Editor"]));
    }

    [Fact]
    public void ShouldMaskCredentialValues_MixedRoles_ViewerPresent_ReturnsTrue()
    {
        Assert.True(_sut.ShouldMaskCredentialValues(["Editor", "Viewer"]));
    }

    [Fact]
    public void ShouldMaskCredentialValues_EmptyRoles_ReturnsFalse()
    {
        Assert.False(_sut.ShouldMaskCredentialValues([]));
    }

    [Fact]
    public void ShouldMaskCredentialValues_CaseInsensitive()
    {
        Assert.True(_sut.ShouldMaskCredentialValues(["viewer"]));
    }

    private async Task<Guid> SeedUserWithRoleAsync(string role, CancellationToken ct)
    {
        var user = new User
        {
            Email = $"{role.ToLower()}@test.com",
            UserName = role.ToLower(),
            PasswordHash = "hash",
            IsActive = true,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(ct);

        _dbContext.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            Role = role,
        });
        await _dbContext.SaveChangesAsync(ct);

        return user.Id;
    }

    private async Task<Guid> SeedProjectAsync(Guid createdBy, CancellationToken ct)
    {
        var project = new Project
        {
            Name = "Test Project",
            CreatedBy = createdBy,
        };
        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(ct);
        return project.Id;
    }

    private async Task<Guid> SeedWorkflowAsync(Guid? projectId, CancellationToken ct)
    {
        var workflow = new Workflow
        {
            Name = "Test Workflow",
            ProjectId = projectId,
            CreatedBy = "tester",
            Version = 1,
            IsActive = true,
            Nodes = [],
            Connections = [],
        };
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);
        return workflow.Id;
    }

    private async Task<Guid> SeedCredentialAsync(Guid? projectId, CancellationToken ct)
    {
        var credential = new Credential
        {
            Name = "Test Credential",
            Type = "apiKey",
            ProjectId = projectId,
            KeyVersion = "v1",
            Data = new Dictionary<string, EncryptedField>
            {
                ["key"] = new() { CipherText = "cipher", Nonce = "nonce", Tag = "tag" },
            },
        };
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);
        return credential.Id;
    }

    private async Task<Guid> SeedExecutionAsync(Guid? projectId, CancellationToken ct)
    {
        var execution = new ExecutionRecord
        {
            WorkflowDefinitionId = Guid.NewGuid(),
            ProjectId = projectId,
            Status = ExecutionStatus.Running,
            StartedAt = DateTime.UtcNow,
            NodeRecords = [],
        };
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);
        return execution.Id;
    }

    private async Task<Guid> SeedTriggerAsync(Guid? projectId, CancellationToken ct)
    {
        var trigger = new Trigger
        {
            Name = "Test Trigger",
            WorkflowDefinitionId = Guid.NewGuid(),
            ProjectId = projectId,
            Type = TriggerType.Webhook,
            Settings = new TriggerSettings(),
        };
        _dbContext.Triggers.Add(trigger);
        await _dbContext.SaveChangesAsync(ct);
        return trigger.Id;
    }
}
