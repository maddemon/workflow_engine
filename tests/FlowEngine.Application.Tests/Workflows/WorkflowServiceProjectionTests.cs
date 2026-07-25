using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using Mapster;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// D-6：验证 WorkflowService.GetAllAsync 列表查询投影到 WorkflowSummaryDto，
/// 不物化 Nodes/Connections 大 JSON 列（通过捕获生成的 SQL 断言）。
/// </summary>
public sealed class WorkflowServiceProjectionTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly List<string> _sqlLog = [];
    private readonly WorkflowService _service;

    public WorkflowServiceProjectionTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite("DataSource=:memory:")
            .LogTo(m => _sqlLog.Add(m), Microsoft.Extensions.Logging.LogLevel.Information)
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _userContext = new FakeUserContext();
        _userContext.Roles = [RoleConstants.Admin];
        var eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(_userContext);
        var scheduleManager = new FakeScheduleManager();
        var resourceAuthorization = new RoleBasedResourceAuthorizationService(_userContext);
        var authGuard = AuthorizationGuardFactory.Create(_userContext, resourceAuthorization, eventBus);
        var triggerService = new TriggerService(
            _dbContext, eventBus, auditFactory, scheduleManager, authGuard, new WebhookRouteService(_dbContext), NullLogger<TriggerService>.Instance);
        var validator = new WorkflowValidator(new FakeNodeRegistry());
        var handler = new AuthorizedOperationHandler(authGuard, eventBus, auditFactory);
        var statisticsLoader = new WorkflowStatisticsLoader(_dbContext);
        var triggerSync = new WorkflowTriggerSync(triggerService, handler);
        _service = new WorkflowService(
            _dbContext, validator, eventBus, auditFactory, triggerService, authGuard, handler, statisticsLoader, triggerSync, NullLogger<WorkflowService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetAllAsync_DoesNotSelectNodesOrConnectionsColumns()
    {
        var ct = TestContext.Current.CancellationToken;

        // 构造一个带大 Nodes JSON 的工作流，强调投影的意义。
        var bigNodes = new List<NodeDefinition>();
        for (var i = 0; i < 50; i++)
        {
            bigNodes.Add(new NodeDefinition
            {
                Id = "n" + i,
                TypeName = "fetch",
                Name = "Fetch" + i,
                Parameters = new Dictionary<string, object> { ["data"] = new string('x', 2000) },
            });
        }

        var createResult = await _service.CreateAsync(new CreateWorkflowDto
        {
            Name = "Big Workflow",
            CreatedBy = "tester",
            Nodes = bigNodes.ConvertAll(n => n.Adapt<NodeDefinitionDto>()),
            Connections = [],
        }, ct);

        _sqlLog.Clear();
        var page = await _service.GetAllAsync(page: 1, pageSize: 20, cancellationToken: ct);

        Assert.Single(page.Items);
        Assert.Equal("Big Workflow", page.Items.ToList()[0].Name);

        // D-6：投影只取摘要字段，SQL 不得包含 Nodes/Connections 大 JSON 列。
        Assert.DoesNotContain(_sqlLog, l => l.Contains("nodes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_sqlLog, l => l.Contains("connections", StringComparison.OrdinalIgnoreCase));
    }
}
