#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// 版本号递增验证（修复 #2：UpdateAsync / ModifyAsync 从不递增 Version）。
/// 内容真正变更时 Version 应递增；无变更时保持不变；GetVersionsAsync 可返回当前版本。
/// 注：当前数据模型以单行的 Version 字段原地自增（非独立历史版本表），
/// 故 GetVersionsAsync 返回含当前版本值的列表。
/// </summary>
public sealed class WorkflowVersionTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly WorkflowService _workflowService;
    private readonly WorkflowModificationService _modificationService;

    public WorkflowVersionTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase($"VersionTestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new FlowEngineDbContext(options);

        var userContext = new FakeUserContext { Roles = [RoleConstants.Admin] };
        var eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(userContext);
        var resourceAuthorization = new RoleBasedResourceAuthorizationService(userContext);
        var authGuard = AuthorizationGuardFactory.Create(userContext, resourceAuthorization, eventBus);
        var scheduleManager = new FakeScheduleManager();
        var triggerService = new TriggerService(
            _dbContext, eventBus, auditFactory, scheduleManager, authGuard, new WebhookRouteService(_dbContext), NullLogger<TriggerService>.Instance);
        var validator = new WorkflowValidator(new StubNodeRegistry([]));
        var handler = new AuthorizedOperationHandler(authGuard, eventBus, auditFactory);
        var statisticsLoader = new WorkflowStatisticsLoader(_dbContext);
        var triggerSync = new WorkflowTriggerSync(triggerService, handler);
        _workflowService = new WorkflowService(
            _dbContext, validator, eventBus, auditFactory, triggerService, authGuard, handler, statisticsLoader, triggerSync, NullLogger<WorkflowService>.Instance);

        var modifyRegistry = new StubNodeRegistry(
        [
            new NodeTypeDescriptor
            {
                TypeName = "webhookTrigger",
                DefaultIsEntry = true,
                Ports = [new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }],
            },
            new NodeTypeDescriptor
            {
                TypeName = "httpRequest",
                Parameters = [new ParameterDefinition { Name = "url", Type = ParameterType.String }],
                Ports =
                [
                    new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                    new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
        ]);
        _modificationService = new WorkflowModificationService(
            modifyRegistry, _dbContext, new WorkflowValidator(modifyRegistry), eventBus, auditFactory, new StubAuthorizationGuard());
    }

    public void Dispose() => _dbContext.Dispose();

    // 正常路径：UpdateAsync 连续多次内容变更，Version 应每次递增（1→2→3），不丢递增。
    [Fact]
    public async Task UpdateAsync_SequentialContentChanges_IncrementVersionEachTime()
    {
        var id = await SeedSimpleWorkflowAsync();
        var before = (await _dbContext.Workflows.FindAsync(id))!.Version;

        for (var i = 1; i <= 3; i++)
        {
            var dto = NewUpdateDto($"Workflow v{i}");
            var updated = await _workflowService.UpdateAsync(id, dto, TestContext.Current.CancellationToken);

            Assert.NotNull(updated);
            Assert.Equal(before + i, updated!.Version);
            var persisted = await _dbContext.Workflows.FindAsync(id);
            Assert.Equal(before + i, persisted!.Version);
        }
    }

    // 边界：UpdateAsync 传入与现有内容完全相同的内容，Version 不应递增（避免无意义版本膨胀）。
    [Fact]
    public async Task UpdateAsync_IdenticalContent_KeepsVersion()
    {
        var id = await SeedSimpleWorkflowAsync();
        var before = (await _dbContext.Workflows.FindAsync(id))!.Version;

        var dto = NewUpdateDto("My Workflow"); // 与 Seed 同名
        var updated = await _workflowService.UpdateAsync(id, dto, TestContext.Current.CancellationToken);

        Assert.Equal(before, updated!.Version);
        var persisted = await _dbContext.Workflows.FindAsync(id);
        Assert.Equal(before, persisted!.Version);
    }

    // 正常路径：ModifyAsync 连续两次结构化修改，Version 应每次递增（1→2）。
    [Fact]
    public async Task ModifyAsync_SequentialModifications_IncrementVersionEachTime()
    {
        var id = await SeedModifyWorkflowAsync();
        var before = (await _dbContext.Workflows.FindAsync(id))!.Version;

        for (var i = 1; i <= 2; i++)
        {
            var request = new ModifyWorkflowRequest
            {
                Operations =
                [
                    new WorkflowOperation
                    {
                        Op = "modify",
                        Path = "/nodes/fetch/parameters/url",
                        Value = $"https://modified-{i}.example.com",
                    },
                ],
            };
            var result = await _modificationService.ModifyAsync(id, request, TestContext.Current.CancellationToken);

            Assert.Equal(before + i, result.Workflow.Version);
            var persisted = await _dbContext.Workflows.FindAsync(id);
            Assert.Equal(before + i, persisted!.Version);
        }
    }

    // 正常路径：GetVersionsAsync 返回当前版本值（数据模型为单行原地自增版本）。
    [Fact]
    public async Task GetVersionsAsync_AfterContentChanges_ReturnsCurrentVersion()
    {
        var id = await SeedSimpleWorkflowAsync();
        await _workflowService.UpdateAsync(id, NewUpdateDto("Workflow v1"), TestContext.Current.CancellationToken);
        await _workflowService.UpdateAsync(id, NewUpdateDto("Workflow v2"), TestContext.Current.CancellationToken);

        var versions = await _workflowService.GetVersionsAsync(id, TestContext.Current.CancellationToken);

        var current = (await _dbContext.Workflows.FindAsync(id))!.Version;
        Assert.Contains(current, versions);
    }

    private async Task<Guid> SeedSimpleWorkflowAsync()
    {
        var dto = new CreateWorkflowDto
        {
            Name = "My Workflow",
            CreatedBy = "tester",
            Nodes = [new NodeDefinitionDto { Id = "n1", TypeName = "fetch", Name = "Fetch", ErrorStrategy = ErrorStrategy.Terminate }],
            Connections = [],
        };
        var created = await _workflowService.CreateAsync(dto, TestContext.Current.CancellationToken);
        return created.Id;
    }

    private async Task<Guid> SeedModifyWorkflowAsync()
    {
        var workflow = new Workflow
        {
            Name = "Seed Workflow",
            CreatedBy = "test",
            IsActive = true,
            Nodes =
            [
                new NodeDefinition { Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger" },
                new NodeDefinition
                {
                    Id = "fetch", TypeName = "httpRequest", Name = "Fetch",
                    Parameters = new() { ["url"] = "https://api.example.com" },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections =
            [
                new Connection { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "fetch", TargetPortName = "input" },
            ],
        };
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return workflow.Id;
    }

    private static UpdateWorkflowDto NewUpdateDto(string name) => new()
    {
        Name = name,
        IsActive = true,
        Nodes = [new NodeDefinitionDto { Id = "n1", TypeName = "fetch", Name = "Fetch", ErrorStrategy = ErrorStrategy.Terminate }],
        Connections = [],
    };

    private sealed class StubNodeRegistry(IReadOnlyCollection<NodeTypeDescriptor> descriptors) : INodeRegistry
    {
        public void Register(INodeType nodeType) { }
        public INodeType Get(string typeName) => throw new InvalidOperationException();
        public bool TryGet(string typeName, out INodeType? nodeType) { nodeType = null; return false; }
        public IReadOnlyCollection<INodeType> GetAll() => [];
        public INodeType CreateInstance(string typeName) => throw new InvalidOperationException();
        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => descriptors;
        public NodeTypeDescriptor GetDescriptor(string typeName) =>
            descriptors.First(d => d.TypeName == typeName);
    }

    private sealed class StubAuthorizationGuard : IAuthorizationGuard
    {
        public Task RequireAccessAsync(ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireScopeAsync(Scope scope, Operation operation, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireAdminAsync(Operation operation, CancellationToken ct = default) => Task.CompletedTask;
    }
}
