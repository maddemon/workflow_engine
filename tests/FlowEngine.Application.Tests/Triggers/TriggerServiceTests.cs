using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;

namespace FlowEngine.Application.Tests.Triggers;

public class TriggerServiceTests
{
    private readonly InMemoryTriggerRepository _repository;
    private readonly InMemoryEventBus _eventBus;
    private readonly TriggerService _service;

    public TriggerServiceTests()
    {
        _repository = new InMemoryTriggerRepository();
        _eventBus = new InMemoryEventBus();
        var userContext = new FakeUserContext();
        var auditFactory = new AuditEventFactory(userContext);
        _service = new TriggerService(_repository, _eventBus, auditFactory);
    }

    [Fact]
    public async Task CreateAsync_ScheduleTrigger_ReturnsDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateTriggerDto
        {
            WorkflowDefinitionId = Guid.NewGuid(),
            WorkflowVersion = 1,
            Type = TriggerType.Schedule,
            Name = "Test Schedule",
            Settings = new TriggerSettingsDto
            {
                CronExpression = "0 */5 * * * ?",
                TimeZone = "UTC",
            },
        };

        var result = await _service.CreateAsync(dto, ct);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(TriggerType.Schedule, result.Type);
        Assert.Equal("Test Schedule", result.Name);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task CreateAsync_WebhookTrigger_CreatesRoute()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.NewGuid();
        var dto = new CreateTriggerDto
        {
            WorkflowDefinitionId = workflowId,
            WorkflowVersion = 1,
            Type = TriggerType.Webhook,
            Name = "Test Webhook",
            Settings = new TriggerSettingsDto
            {
                WebhookPath = "/webhooks/test",
                Secret = "my-secret",
                IsSync = false,
            },
        };

        var result = await _service.CreateAsync(dto, ct);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(TriggerType.Webhook, result.Type);

        var routes = await _repository.GetWebhookRoutesByWorkflowDefinitionIdAsync(workflowId, ct);
        Assert.Single(routes);
        Assert.Equal("/webhooks/test", routes.First().Path);
        Assert.Equal("my-secret", routes.First().Secret);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingTrigger_ReturnsDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var trigger = CreateTestTrigger(TriggerType.Schedule);
        await _repository.SaveAsync(trigger, ct);

        var result = await _service.GetByIdAsync(trigger.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(trigger.Id, result.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingTrigger_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _service.GetByIdAsync(Guid.NewGuid(), ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByWorkflowDefinitionIdAsync_ReturnsMatchingTriggers()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.NewGuid();
        var trigger1 = CreateTestTrigger(TriggerType.Schedule, workflowId);
        var trigger2 = CreateTestTrigger(TriggerType.Webhook, workflowId);
        var trigger3 = CreateTestTrigger(TriggerType.Schedule, Guid.NewGuid());
        await _repository.SaveAsync(trigger1, ct);
        await _repository.SaveAsync(trigger2, ct);
        await _repository.SaveAsync(trigger3, ct);

        var results = await _service.GetByWorkflowDefinitionIdAsync(workflowId, ct);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task UpdateAsync_ExistingTrigger_UpdatesFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var trigger = CreateTestTrigger(TriggerType.Schedule);
        await _repository.SaveAsync(trigger, ct);

        var dto = new UpdateTriggerDto
        {
            Name = "Updated Name",
            IsActive = false,
            Settings = new TriggerSettingsDto
            {
                CronExpression = "0 */10 * * * ?",
            },
        };

        var result = await _service.UpdateAsync(trigger.Id, dto, ct);

        Assert.NotNull(result);
        Assert.Equal("Updated Name", result.Name);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingTrigger_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new UpdateTriggerDto
        {
            Name = "Test",
            IsActive = true,
        };

        var result = await _service.UpdateAsync(Guid.NewGuid(), dto, ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_ExistingTrigger_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var trigger = CreateTestTrigger(TriggerType.Schedule);
        await _repository.SaveAsync(trigger, ct);

        var result = await _service.DeleteAsync(trigger.Id, ct);

        Assert.True(result);
        var deleted = await _repository.GetByIdAsync(trigger.Id, ct);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingTrigger_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _service.DeleteAsync(Guid.NewGuid(), ct);
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteByWorkflowDefinitionIdAsync_DeletesAllTriggers()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.NewGuid();
        var trigger1 = CreateTestTrigger(TriggerType.Schedule, workflowId);
        var trigger2 = CreateTestTrigger(TriggerType.Webhook, workflowId);
        await _repository.SaveAsync(trigger1, ct);
        await _repository.SaveAsync(trigger2, ct);

        await _service.DeleteByWorkflowDefinitionIdAsync(workflowId, ct);

        var remaining = await _repository.GetByWorkflowDefinitionIdAsync(workflowId, ct);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyActiveTriggers()
    {
        var ct = TestContext.Current.CancellationToken;
        var active = CreateTestTrigger(TriggerType.Schedule);
        active.IsActive = true;
        var inactive = CreateTestTrigger(TriggerType.Schedule);
        inactive.IsActive = false;
        await _repository.SaveAsync(active, ct);
        await _repository.SaveAsync(inactive, ct);

        var results = await _service.GetActiveAsync(ct);

        Assert.Single(results);
        Assert.Equal(active.Id, results.First().Id);
    }

    [Fact]
    public async Task CreateAsync_PublishesAuditEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateTriggerDto
        {
            WorkflowDefinitionId = Guid.NewGuid(),
            WorkflowVersion = 1,
            Type = TriggerType.Schedule,
            Name = "Test",
        };

        await _service.CreateAsync(dto, ct);

        Assert.True(_eventBus.PublishedEvents.Count > 0);
    }

    private static Trigger CreateTestTrigger(TriggerType type, Guid? workflowId = null)
    {
        return new Trigger
        {
            WorkflowDefinitionId = workflowId ?? Guid.NewGuid(),
            WorkflowVersion = 1,
            Type = type,
            Name = $"Test {type}",
            IsActive = true,
            SettingsJson = "{}",
        };
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles => [];
    }

    private sealed class InMemoryTriggerRepository : ITriggerRepository
    {
        private readonly Dictionary<Guid, Trigger> _triggers = new();
        private readonly Dictionary<Guid, WebhookRoute> _routes = new();

        public Task<Trigger?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _triggers.TryGetValue(id, out var trigger);
            return Task.FromResult(trigger);
        }

        public Task<IReadOnlyCollection<Trigger>> GetByWorkflowDefinitionIdAsync(
            Guid workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            var result = _triggers.Values
                .Where(t => t.WorkflowDefinitionId == workflowDefinitionId)
                .ToList();
            return Task.FromResult<IReadOnlyCollection<Trigger>>(result);
        }

        public Task<IReadOnlyCollection<Trigger>> GetActiveAsync(CancellationToken cancellationToken = default)
        {
            var result = _triggers.Values.Where(t => t.IsActive).ToList();
            return Task.FromResult<IReadOnlyCollection<Trigger>>(result);
        }

        public Task SaveAsync(Trigger trigger, CancellationToken cancellationToken = default)
        {
            _triggers[trigger.Id] = trigger;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _triggers.Remove(id);
            return Task.CompletedTask;
        }

        public Task DeleteByWorkflowDefinitionIdAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            var keys = _triggers.Values
                .Where(t => t.WorkflowDefinitionId == workflowDefinitionId)
                .Select(t => t.Id)
                .ToList();
            foreach (var key in keys) _triggers.Remove(key);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<WebhookRoute>> GetWebhookRoutesByWorkflowDefinitionIdAsync(
            Guid workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            var result = _routes.Values
                .Where(r => r.WorkflowDefinitionId == workflowDefinitionId)
                .ToList();
            return Task.FromResult<IReadOnlyCollection<WebhookRoute>>(result);
        }

        public Task<WebhookRoute?> GetWebhookRouteByPathAsync(string path, CancellationToken cancellationToken = default)
        {
            var result = _routes.Values.FirstOrDefault(r => r.Path == path);
            return Task.FromResult(result);
        }

        public Task<IReadOnlyCollection<WebhookRoute>> GetAllWebhookRoutesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<WebhookRoute>>(_routes.Values.ToList());
        }

        public Task SaveWebhookRouteAsync(WebhookRoute route, CancellationToken cancellationToken = default)
        {
            _routes[route.Id] = route;
            return Task.CompletedTask;
        }

        public Task DeleteWebhookRouteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _routes.Remove(id);
            return Task.CompletedTask;
        }

        public Task DeleteWebhookRoutesByWorkflowDefinitionIdAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            var keys = _routes.Values
                .Where(r => r.WorkflowDefinitionId == workflowDefinitionId)
                .Select(r => r.Id)
                .ToList();
            foreach (var key in keys) _routes.Remove(key);
            return Task.CompletedTask;
        }

        public Task DeleteWebhookRoutesByTriggerIdAsync(Guid triggerId, CancellationToken cancellationToken = default)
        {
            var keys = _routes.Values
                .Where(r => r.TriggerId == triggerId)
                .Select(r => r.Id)
                .ToList();
            foreach (var key in keys) _routes.Remove(key);
            return Task.CompletedTask;
        }
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
            where TEvent : IDomainEvent
        {
            return new Disposable();
        }

        private sealed class Disposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
