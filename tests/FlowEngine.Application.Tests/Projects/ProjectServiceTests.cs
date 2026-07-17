using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Validators;
using FlowEngine.Application.Projects;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Projects;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly InMemoryEventBus _eventBus;
    private readonly FakeUserContext _userContext;
    private readonly ProjectService _service;

    public ProjectServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new InMemoryEventBus();
        _userContext = new FakeUserContext();
        var auditFactory = new AuditEventFactory(_userContext);
        var authGuard = AuthorizationGuardFactory.Create(_userContext, new FakeResourceAuthorizationService(_dbContext, _userContext));
        var handler = new AuthorizedOperationHandler(authGuard, _eventBus, auditFactory);
        var cascadeDeleter = new ProjectCascadeDeleter(_dbContext);
        _service = new ProjectService(_dbContext, _userContext, authGuard, _eventBus, auditFactory, handler, cascadeDeleter, new CreateProjectDtoValidator());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsync_ValidDto_CreatesProjectWithoutMember()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = _userContext.UserId!.Value;
        var dto = new CreateProjectDto { Name = "Test Project", Description = "A test project" };

        var result = await _service.CreateAsync(dto, ct);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Test Project", result.Name);
        Assert.Equal("A test project", result.Description);
        Assert.Equal(userId, result.CreatedBy);
    }

    [Fact]
    public async Task CreateAsync_NullDto_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateAsync(null!, ct));
    }

    [Fact]
    public async Task CreateAsync_PublishesAuditEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateProjectDto { Name = "Test" };

        var result = await _service.CreateAsync(dto, ct);

        var auditEvent = Assert.Single(_eventBus.PublishedEvents);
        var logEvent = Assert.IsType<AuditLogEvent>(auditEvent);
        Assert.Equal(AuditEventTypes.ProjectCreated, logEvent.EventType);
        Assert.Equal("Project", logEvent.ResourceType);
        Assert.Equal(result.Id, logEvent.ResourceId);
        Assert.NotNull(logEvent.Payload);
        Assert.Equal("Test", logEvent.Payload["name"].ToString());
    }

    [Fact]
    public async Task GetAllAsync_SystemAdmin_ReturnsAllProjects()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.Roles = ["Admin"];
        var userId = _userContext.UserId!.Value;

        var project1 = new Project { Name = "Project 1", CreatedBy = userId };
        var project2 = new Project { Name = "Project 2", CreatedBy = Guid.NewGuid() };
        _dbContext.Projects.AddRange(project1, project2);
        await _dbContext.SaveChangesAsync(ct);

        var result = await _service.GetAllAsync(ct);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingProject_ReturnsDto()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.Roles = ["Editor"];
        var project = new Project { Name = "Test", CreatedBy = _userContext.UserId!.Value };
        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(ct);

        var result = await _service.GetByIdAsync(project.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(project.Id, result.Id);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingProject_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _service.GetByIdAsync(Guid.NewGuid(), ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_OtherUsersProject_ThrowsPermissionDenied()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.Roles = ["Editor"];
        var project = new Project { Name = "Test", CreatedBy = Guid.NewGuid() };
        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(ct);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.GetByIdAsync(project.Id, ct));
    }

    [Fact]
    public async Task UpdateAsync_ExistingProject_UpdatesFields()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.Roles = ["Editor"];
        var project = new Project { Name = "Original", CreatedBy = _userContext.UserId!.Value };
        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(ct);

        var dto = new UpdateProjectDto { Name = "Updated", Description = "New desc" };

        var result = await _service.UpdateAsync(project.Id, dto, ct);

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Name);
        Assert.Equal("New desc", result.Description);
        Assert.NotNull(result.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingProject_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new UpdateProjectDto { Name = "Test" };
        var result = await _service.UpdateAsync(Guid.NewGuid(), dto, ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_SystemAdmin_DeletesProject()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.Roles = ["Admin"];
        var project = new Project { Name = "To Delete", CreatedBy = Guid.NewGuid() };
        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(ct);

        var result = await _service.DeleteAsync(project.Id, ct);

        Assert.True(result);
        var deleted = await _dbContext.Projects.FindAsync([project.Id], ct);
        Assert.NotNull(deleted);
        Assert.True(deleted.Deleted);
    }

    [Fact]
    public async Task DeleteAsync_NonSystemAdmin_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.Roles = ["Editor"];
        var project = new Project { Name = "To Delete", CreatedBy = Guid.NewGuid() };
        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(ct);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.DeleteAsync(project.Id, ct));
    }

    [Fact]
    public async Task DeleteAsync_NonExistingProject_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.Roles = ["Admin"];
        var result = await _service.DeleteAsync(Guid.NewGuid(), ct);
        Assert.False(result);
    }


    private sealed class InMemoryEventBus : IEventBus
    {
        public List<object> PublishedEvents { get; } = [];

        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            PublishedEvents.Add(eventInstance!);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent => new Disposable();

        private sealed class Disposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class FakeUserContext : IUserContext
    {
        private readonly Guid _userId = Guid.NewGuid();

        public bool IsAuthenticated => true;
        public Guid? UserId => _userId;
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles { get; set; } = [];
    }

    private sealed class FakeResourceAuthorizationService(FlowEngineDbContext dbContext, FakeUserContext userContext) : IResourceAuthorizationService
    {
        public Task<bool> CanAccessProjectAsync(Guid userId, Guid projectId, Operation operation, CancellationToken ct = default)
        {
            if (userContext.Roles.Contains(RoleConstants.Admin))
            {
                return Task.FromResult(true);
            }

            if (!userContext.Roles.Any())
            {
                return Task.FromResult(false);
            }

            var project = dbContext.Projects
                .AsNoTracking()
                .FirstOrDefault(p => p.Id == projectId && !p.Deleted);

            return Task.FromResult(project is not null && project.CreatedBy == userId);
        }

        public Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles) => false;
    }
}
