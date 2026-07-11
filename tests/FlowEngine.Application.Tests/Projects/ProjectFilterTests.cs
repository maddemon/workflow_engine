using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Credentials;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Credentials;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Projects;

public sealed class ProjectFilterTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;

    public ProjectFilterTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task WorkflowService_GetAllAsync_WithProjectId_ReturnsOnlyMatchingProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        _dbContext.Workflows.AddRange(
            CreateWorkflow("Workflow A", projectId),
            CreateWorkflow("Workflow B", otherProjectId),
            CreateWorkflow("Workflow C", projectId));
        await _dbContext.SaveChangesAsync(ct);

        var service = CreateWorkflowService();

        var result = await service.GetAllAsync(projectId, 1, 20, ct);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(projectId, item.ProjectId));
    }

    [Fact]
    public async Task WorkflowService_GetAllAsync_WithoutProjectId_ReturnsAll()
    {
        var ct = TestContext.Current.CancellationToken;
        _dbContext.Workflows.AddRange(
            CreateWorkflow("Workflow A", Guid.NewGuid()),
            CreateWorkflow("Workflow B", Guid.NewGuid()));
        await _dbContext.SaveChangesAsync(ct);

        var service = CreateWorkflowService();

        var result = await service.GetAllAsync(null, 1, 20, ct);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task CredentialService_GetAllAsync_WithProjectId_ReturnsOnlyMatchingProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        _dbContext.Credentials.AddRange(
            CreateCredential("Key A", projectId),
            CreateCredential("Key B", otherProjectId),
            CreateCredential("Key C", projectId));
        await _dbContext.SaveChangesAsync(ct);

        var service = CreateCredentialService();

        var result = await service.GetAllAsync(projectId, ct);

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Equal(projectId, item.ProjectId));
    }

    [Fact]
    public async Task CredentialService_GetAllAsync_WithoutProjectId_ReturnsAll()
    {
        var ct = TestContext.Current.CancellationToken;
        _dbContext.Credentials.AddRange(
            CreateCredential("Key A", Guid.NewGuid()),
            CreateCredential("Key B", Guid.NewGuid()));
        await _dbContext.SaveChangesAsync(ct);

        var service = CreateCredentialService();

        var result = await service.GetAllAsync(cancellationToken: ct);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task TriggerService_GetAllForUserAsync_WithProjectId_ReturnsOnlyMatchingProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var workflowA = CreateWorkflow("Workflow A", projectId);
        var workflowB = CreateWorkflow("Workflow B", otherProjectId);
        _dbContext.Workflows.AddRange(workflowA, workflowB);
        var triggerA = CreateTrigger(workflowA.Id, projectId);
        var triggerB = CreateTrigger(workflowB.Id, otherProjectId);
        var triggerC = CreateTrigger(workflowA.Id, projectId);
        _dbContext.Triggers.AddRange(triggerA, triggerB, triggerC);
        await _dbContext.SaveChangesAsync(ct);

        var service = CreateTriggerService();

        var result = await service.GetAllForUserAsync(projectId, ct);
        var resultIds = result.Select(r => r.Id).ToHashSet();

        Assert.Equal(2, result.Count);
        Assert.Contains(triggerA.Id, resultIds);
        Assert.Contains(triggerC.Id, resultIds);
        Assert.DoesNotContain(triggerB.Id, resultIds);
    }

    [Fact]
    public async Task TriggerService_GetAllForUserAsync_WithoutProjectId_ReturnsAll()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowA = CreateWorkflow("Workflow A", Guid.NewGuid());
        var workflowB = CreateWorkflow("Workflow B", Guid.NewGuid());
        _dbContext.Workflows.AddRange(workflowA, workflowB);
        _dbContext.Triggers.AddRange(
            CreateTrigger(workflowA.Id, workflowA.ProjectId),
            CreateTrigger(workflowB.Id, workflowB.ProjectId));
        await _dbContext.SaveChangesAsync(ct);

        var service = CreateTriggerService();

        var result = await service.GetAllForUserAsync(cancellationToken: ct);

        Assert.Equal(2, result.Count);
    }

    private WorkflowService CreateWorkflowService()
    {
        var userContext = new FakeUserContext();
        var resourceAuthorization = new StubResourceAuthorizationService();
        var eventBus = new InMemoryEventBus();
        var auditFactory = new AuditEventFactory(userContext);
        var authGuard = AuthorizationGuardFactory.Create(userContext, resourceAuthorization);
        var handler = new AuthorizedOperationHandler(authGuard, eventBus, auditFactory);
        var triggerService = CreateTriggerService();
        var statisticsLoader = new WorkflowStatisticsLoader(_dbContext);
        var triggerSync = new WorkflowTriggerSync(triggerService, handler);
        return new WorkflowService(
            _dbContext,
            new WorkflowValidator(new FakeNodeRegistry()),
            eventBus,
            auditFactory,
            triggerService,
            authGuard,
            handler,
            statisticsLoader,
            triggerSync);
    }

    private CredentialService CreateCredentialService()
    {
        var userContext = new FakeUserContext();
        var eventBus = new InMemoryEventBus();
        var auditFactory = new AuditEventFactory(userContext);
        var authGuard = AuthorizationGuardFactory.Create(userContext, new StubResourceAuthorizationService());
        var handler = new AuthorizedOperationHandler(authGuard, eventBus, auditFactory);
        return new CredentialService(
            _dbContext,
            new StubEncryptionService(),
            new StubKeyProvider(),
            eventBus,
            auditFactory,
            new StubResourceAuthorizationService(),
            userContext,
            new WorkflowRepository(_dbContext),
            authGuard,
            new CredentialTypeRegistry(),
            handler);
    }

    private TriggerService CreateTriggerService()
    {
        var userContext = new FakeUserContext();
        var resourceAuthorization = new StubResourceAuthorizationService();
        return new TriggerService(
            _dbContext,
            new InMemoryEventBus(),
            new AuditEventFactory(userContext),
            new FakeScheduleManager(),
            AuthorizationGuardFactory.Create(userContext, resourceAuthorization),
            new WebhookRouteService(_dbContext));
    }

    private static Workflow CreateWorkflow(string name, Guid? projectId)
    {
        return new Workflow
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = name,
            CreatedBy = "test-user",
            IsActive = true,
            Nodes = [],
            Connections = [],
        };
    }

    private static Credential CreateCredential(string name, Guid? projectId)
    {
        return new Credential
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = name,
            Type = "apiKey",
            Data = new Dictionary<string, EncryptedField>(),
            KeyVersion = "v1",
        };
    }

    private static Trigger CreateTrigger(Guid workflowDefinitionId, Guid? projectId)
    {
        return new Trigger
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflowDefinitionId,
            ProjectId = projectId,
            WorkflowVersion = 1,
            Type = TriggerType.Schedule,
            Name = "Test Trigger",
            IsActive = true,
            Settings = new TriggerSettings(),
        };
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles => [RoleConstants.Admin];
    }

    private sealed class InMemoryEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent
        {
            return new Disposable();
        }

        private sealed class Disposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class FakeNodeRegistry : INodeRegistry
    {
        public void Register(INodeType nodeType) { }

        public INodeType Get(string typeName) => throw new InvalidOperationException();

        public bool TryGet(string typeName, out INodeType? nodeType)
        {
            nodeType = null;
            return false;
        }

        public IReadOnlyCollection<INodeType> GetAll() => [];

        public INodeType CreateInstance(string typeName) => throw new InvalidOperationException();

        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => [];

        public NodeTypeDescriptor GetDescriptor(string typeName) => throw new InvalidOperationException();
    }

    private sealed class StubEncryptionService : ICredentialEncryptionService
    {
        public EncryptedField Encrypt(string plaintext, byte[] key)
        {
            return new EncryptedField
            {
                CipherText = $"encrypted:{plaintext}",
                Nonce = "nonce",
                Tag = "tag",
            };
        }

        public EncryptedField Encrypt(byte[] plaintext, byte[] key) =>
            new() { CipherText = Convert.ToBase64String(plaintext), Nonce = "nonce", Tag = "tag" };

        public string DecryptString(EncryptedField field, byte[] key) =>
            field.CipherText.Replace("encrypted:", "");

        public byte[] DecryptBytes(EncryptedField field, byte[] key) =>
            Convert.FromBase64String(field.CipherText);
    }

    private sealed class StubKeyProvider : ICryptoKeyProvider
    {
        public byte[] GetKey() => new byte[32];
    }

    private sealed class StubResourceAuthorizationService : IResourceAuthorizationService
    {
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

    private sealed class FakeScheduleManager : IScheduleManager
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterScheduleAsync(Guid triggerId, Guid workflowDefinitionId, string cronExpression, string? timeZone, DateTime? startAt, DateTime? endAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.FromResult<DateTime?>(null);
        public Task RegisterPollTriggerAsync(Guid triggerId, Guid workflowDefinitionId, int intervalSeconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
