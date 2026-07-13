using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Exceptions;
using FlowEngine.Host.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Moq;

namespace FlowEngine.Host.Tests.Mcp;

/// <summary>
/// Workflow 查询与生命周期 MCP 工具单元测试。
/// </summary>
public class WorkflowQueryToolsTests
{
    // ── get_workflow 成功路径 ──────────────────────────────────────

    /// <summary>
    /// get_workflow 在工作流存在时应返回 WorkflowDto。
    /// </summary>
    [Fact]
    public async Task GetWorkflow_ExistingWorkflow_ReturnsDto()
    {
        var workflowId = Guid.NewGuid();
        var expected = new WorkflowDto
        {
            Id = workflowId,
            Name = "test-flow",
            Nodes = [],
            Connections = [],
        };

        var serviceMock = new Mock<IWorkflowService>();
        serviceMock
            .Setup(s => s.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tools = new WorkflowQueryTools(serviceMock.Object);
        var result = await tools.GetWorkflow(workflowId.ToString(), CancellationToken.None);

        Assert.Same(expected, result);
    }

    // ── get_workflow 未找到 ─────────────────────────────────────────

    /// <summary>
    /// get_workflow 在工作流不存在时应返回 NotFound 结构化错误。
    /// </summary>
    [Fact]
    public async Task GetWorkflow_NonExistingWorkflow_ReturnsNotFoundError()
    {
        var workflowId = Guid.NewGuid();
        var serviceMock = new Mock<IWorkflowService>();
        serviceMock
            .Setup(s => s.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDto?)null);

        var tools = new WorkflowQueryTools(serviceMock.Object);
        var result = await tools.GetWorkflow(workflowId.ToString(), CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("NotFound", element.GetProperty("errorCode").GetString());
    }

    // ── get_workflow 非法 Guid ──────────────────────────────────────

    /// <summary>
    /// get_workflow 在 workflowId 为非法 Guid 时应返回 InvalidInput 错误。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    public async Task GetWorkflow_InvalidWorkflowId_ReturnsInvalidInputError(string? workflowId)
    {
        var tools = new WorkflowQueryTools(Mock.Of<IWorkflowService>());
        var result = await tools.GetWorkflow(workflowId!, CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    // ── list_workflows 成功路径 ────────────────────────────────────

    /// <summary>
    /// list_workflows 在无过滤时应返回分页结果。
    /// </summary>
    [Fact]
    public async Task ListWorkflows_NoFilter_ReturnsPagedResult()
    {
        var expectedResult = new PagedResult<WorkflowSummaryDto>
        {
            Items = [new WorkflowSummaryDto { Id = Guid.NewGuid(), Name = "flow1" }],
            TotalCount = 1,
            Page = 1,
            PageSize = 20,
        };

        var serviceMock = new Mock<IWorkflowService>();
        serviceMock
            .Setup(s => s.GetAllAsync(null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var tools = new WorkflowQueryTools(serviceMock.Object);
        var result = await tools.ListWorkflows(cancellationToken: CancellationToken.None);

        Assert.Same(expectedResult, result);
    }

    /// <summary>
    /// list_workflows 在传入 projectId 过滤时应转发给服务。
    /// </summary>
    [Fact]
    public async Task ListWorkflows_WithProjectId_PassesFilterToService()
    {
        var projectId = Guid.NewGuid();
        var expectedResult = new PagedResult<WorkflowSummaryDto>
        {
            Items = [],
            TotalCount = 0,
        };

        var serviceMock = new Mock<IWorkflowService>();
        serviceMock
            .Setup(s => s.GetAllAsync(projectId, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var tools = new WorkflowQueryTools(serviceMock.Object);
        var result = await tools.ListWorkflows(
            projectId: projectId.ToString(),
            page: 2,
            pageSize: 10,
            cancellationToken: CancellationToken.None);

        Assert.Same(expectedResult, result);
        serviceMock.Verify(s => s.GetAllAsync(projectId, 2, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── list_workflows 非法 projectId ──────────────────────────────

    /// <summary>
    /// list_workflows 在 projectId 为非法 Guid 时应返回 InvalidInput 错误。
    /// </summary>
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    public async Task ListWorkflows_InvalidProjectId_ReturnsInvalidInputError(string projectId)
    {
        var tools = new WorkflowQueryTools(Mock.Of<IWorkflowService>());
        var result = await tools.ListWorkflows(projectId: projectId, cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    // ── list_workflows pageSize 范围校验 ──────────────────────────

    /// <summary>
    /// list_workflows 在 pageSize 超出 1–200 范围时应返回 InvalidInput 错误。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    [InlineData(500)]
    public async Task ListWorkflows_PageSizeOutOfRange_ReturnsInvalidInputError(int pageSize)
    {
        var tools = new WorkflowQueryTools(Mock.Of<IWorkflowService>());
        var result = await tools.ListWorkflows(pageSize: pageSize, cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
        Assert.Contains("pageSize", element.GetProperty("message").GetString());
    }

    // ── MCP 工具注册验证 ──────────────────────────────────────────

    /// <summary>
    /// WorkflowQueryTools 的方法应被 MCP SDK 识别并注册指定名称。
    /// </summary>
    [Theory]
    [InlineData(nameof(WorkflowQueryTools.GetWorkflow), "get_workflow")]
    [InlineData(nameof(WorkflowQueryTools.ListWorkflows), "list_workflows")]
    public void QueryToolMethods_AreDiscoveredWithExpectedNames(string methodName, string expectedToolName)
    {
        var typeInfo = typeof(WorkflowQueryTools);
        Assert.NotNull(typeInfo.GetCustomAttribute<McpServerToolTypeAttribute>());

        var method = typeInfo.GetMethod(methodName);
        Assert.NotNull(method);

        var toolAttribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(toolAttribute);
        Assert.Equal(expectedToolName, toolAttribute!.Name);

        var descriptionAttribute = method.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(descriptionAttribute);
        Assert.False(string.IsNullOrWhiteSpace(descriptionAttribute!.Description));
    }
}

/// <summary>
/// Workflow 生命周期 MCP 工具单元测试。
/// </summary>
public class WorkflowLifecycleToolsTests
{
    // ── validate_workflow 成功路径 ────────────────────────────────

    /// <summary>
    /// validate_workflow 在传入 workflowId 时应返回 ValidateWorkflowResult。
    /// </summary>
    [Fact]
    public async Task ValidateWorkflow_WithWorkflowId_ReturnsResult()
    {
        var workflowId = Guid.NewGuid();
        var expectedResult = new ValidateWorkflowResult { Valid = true, Errors = [] };

        var validationMock = new Mock<IWorkflowValidationService>();
        validationMock
            .Setup(s => s.ValidateAsync(It.IsAny<ValidateWorkflowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var tools = CreateLifecycleTools(validationService: validationMock.Object);
        var result = await tools.ValidateWorkflow(workflowId: workflowId.ToString(), cancellationToken: CancellationToken.None);

        Assert.Same(expectedResult, result);
        Assert.True(result.Valid);
    }

    /// <summary>
    /// validate_workflow 在传入 nodes/connections 时应返回校验结果。
    /// </summary>
    [Fact]
    public async Task ValidateWorkflow_WithDraftNodes_ReturnsResult()
    {
        var expectedResult = new ValidateWorkflowResult
        {
            Valid = false,
            Errors = [new ValidationError { ErrorType = "MissingRequired", Message = "缺少触发器" }],
        };

        var validationMock = new Mock<IWorkflowValidationService>();
        validationMock
            .Setup(s => s.ValidateAsync(It.IsAny<ValidateWorkflowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var tools = CreateLifecycleTools(validationService: validationMock.Object);
        var result = await tools.ValidateWorkflow(
            nodes: [new NodeDefinitionDto { Id = "n1", TypeName = "httpRequest" }],
            connections: null,
            cancellationToken: CancellationToken.None);

        Assert.Same(expectedResult, result);
        Assert.False(result.Valid);
    }

    /// <summary>
    /// validate_workflow 在 workflowId 为非法 Guid 时应返回 InvalidInput 校验错误，不抛异常。
    /// </summary>
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    public async Task ValidateWorkflow_InvalidWorkflowId_ReturnsValidationErrors(string workflowId)
    {
        var tools = CreateLifecycleTools();
        var result = await tools.ValidateWorkflow(workflowId: workflowId, cancellationToken: CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Single(result.Errors);
        Assert.Equal("InvalidInput", result.Errors[0].ErrorType);
    }

    // ── confirm_workflow 成功路径 ──────────────────────────────────

    /// <summary>
    /// confirm_workflow 在草稿存在时应返回激活后的 WorkflowDto。
    /// </summary>
    [Fact]
    public async Task ConfirmWorkflow_ExistingDraft_ReturnsActivatedDto()
    {
        var draftId = Guid.NewGuid();
        var expected = new WorkflowDto { Id = draftId, Name = "flow", IsActive = true };

        var workflowMock = new Mock<IWorkflowService>();
        workflowMock
            .Setup(s => s.ConfirmDraftAsync(draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tools = CreateLifecycleTools(workflowService: workflowMock.Object);
        var result = await tools.ConfirmWorkflow(draftId.ToString(), CancellationToken.None);

        Assert.Same(expected, result);
    }

    // ── confirm_workflow 未找到 ─────────────────────────────────────

    /// <summary>
    /// confirm_workflow 在草稿不存在时应返回 NotFound 结构化错误。
    /// </summary>
    [Fact]
    public async Task ConfirmWorkflow_NonExistingDraft_ReturnsNotFoundError()
    {
        var draftId = Guid.NewGuid();
        var workflowMock = new Mock<IWorkflowService>();
        workflowMock
            .Setup(s => s.ConfirmDraftAsync(draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDto?)null);

        var tools = CreateLifecycleTools(workflowService: workflowMock.Object);
        var result = await tools.ConfirmWorkflow(draftId.ToString(), CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("NotFound", element.GetProperty("errorCode").GetString());
    }

    // ── confirm_workflow 非法 Guid ──────────────────────────────────

    /// <summary>
    /// confirm_workflow 在 draftId 为非法 Guid 时应返回 InvalidInput 错误。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    public async Task ConfirmWorkflow_InvalidDraftId_ReturnsInvalidInputError(string? draftId)
    {
        var tools = CreateLifecycleTools();
        var result = await tools.ConfirmWorkflow(draftId!, CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    // ── execute_workflow 成功路径 ──────────────────────────────────

    /// <summary>
    /// execute_workflow 在工作流存在时应返回 ExecutionDto。
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflow_ExistingWorkflow_ReturnsExecutionDto()
    {
        var workflowId = Guid.NewGuid();
        var expected = new ExecutionDto
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflowId,
            Status = "Pending",
            StartedAt = DateTime.UtcNow,
        };

        var executionMock = new Mock<IExecutionService>();
        executionMock
            .Setup(s => s.ExecuteAsync(workflowId, null, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(expected);

        var tools = CreateLifecycleTools(executionService: executionMock.Object);
        var result = await tools.ExecuteWorkflow(workflowId.ToString(), cancellationToken: CancellationToken.None);

        Assert.Same(expected, result);
    }

    /// <summary>
    /// execute_workflow 在传入 inputs 和 idempotencyKey 时应正确转发。
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflow_WithInputsAndIdempotencyKey_PassesThemThrough()
    {
        var workflowId = Guid.NewGuid();
        var inputs = new Dictionary<string, object> { ["key"] = "value" };
        var idempotencyKey = "idem-123";

        var expected = new ExecutionDto
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflowId,
            Status = "Running",
        };

        var executionMock = new Mock<IExecutionService>();
        executionMock
            .Setup(s => s.ExecuteAsync(workflowId, idempotencyKey, It.IsAny<CancellationToken>(), inputs))
            .ReturnsAsync(expected);

        var tools = CreateLifecycleTools(executionService: executionMock.Object);
        var result = await tools.ExecuteWorkflow(
            workflowId.ToString(),
            inputs: inputs,
            idempotencyKey: idempotencyKey,
            cancellationToken: CancellationToken.None);

        Assert.Same(expected, result);
        executionMock.Verify(
            s => s.ExecuteAsync(workflowId, idempotencyKey, It.IsAny<CancellationToken>(), inputs),
            Times.Once);
    }

    // ── execute_workflow 工作流不存在 ──────────────────────────────

    /// <summary>
    /// execute_workflow 在工作流不存在时应返回 NotFound 结构化错误，包含 executionContext 和 suggestedFix 字段。
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflow_NonExistingWorkflow_ReturnsNotFoundError()
    {
        var workflowId = Guid.NewGuid();
        var executionMock = new Mock<IExecutionService>();
        executionMock
            .Setup(s => s.ExecuteAsync(workflowId, null, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync((ExecutionDto?)null);

        var tools = CreateLifecycleTools(executionService: executionMock.Object);
        var result = await tools.ExecuteWorkflow(workflowId.ToString(), cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("NotFound", element.GetProperty("errorCode").GetString());
        Assert.True(element.TryGetProperty("executionContext", out var ctx) && ctx.ValueKind == JsonValueKind.Null);
        Assert.True(element.TryGetProperty("suggestedFix", out var fix) && fix.ValueKind == JsonValueKind.Null);
    }

    // ── execute_workflow 非法 Guid ──────────────────────────────────

    /// <summary>
    /// execute_workflow 在 workflowId 为非法 Guid 时应返回 InvalidInput 错误。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    public async Task ExecuteWorkflow_InvalidWorkflowId_ReturnsInvalidInputError(string? workflowId)
    {
        var tools = CreateLifecycleTools();
        var result = await tools.ExecuteWorkflow(workflowId!, cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    // ── execute_workflow BusinessException 捕获 ────────────────────

    /// <summary>
    /// execute_workflow 在服务抛出 BusinessException 时应返回 ExecutionFailed 结构化错误（含 executionContext 和 suggestedFix），不泄漏异常。
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflow_BusinessException_ReturnsExecutionFailedError()
    {
        var workflowId = Guid.NewGuid();
        var executionMock = new Mock<IExecutionService>();
        executionMock
            .Setup(s => s.ExecuteAsync(workflowId, null, It.IsAny<CancellationToken>(), null))
            .ThrowsAsync(new BusinessException("工作流未激活"));

        var tools = CreateLifecycleTools(executionService: executionMock.Object);
        var result = await tools.ExecuteWorkflow(workflowId.ToString(), cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("ExecutionFailed", element.GetProperty("errorCode").GetString());
        Assert.Contains("工作流未激活", element.GetProperty("message").GetString());
        Assert.True(element.TryGetProperty("executionContext", out var ctx) && ctx.ValueKind == JsonValueKind.Null);
        Assert.True(element.TryGetProperty("suggestedFix", out var fix) && fix.ValueKind == JsonValueKind.Null);
    }

    // ── MCP 工具注册验证 ──────────────────────────────────────────

    /// <summary>
    /// WorkflowLifecycleTools 的方法应被 MCP SDK 识别并注册指定名称。
    /// </summary>
    [Theory]
    [InlineData(nameof(WorkflowLifecycleTools.ValidateWorkflow), "validate_workflow")]
    [InlineData(nameof(WorkflowLifecycleTools.ConfirmWorkflow), "confirm_workflow")]
    [InlineData(nameof(WorkflowLifecycleTools.ExecuteWorkflow), "execute_workflow")]
    public void LifecycleToolMethods_AreDiscoveredWithExpectedNames(string methodName, string expectedToolName)
    {
        var typeInfo = typeof(WorkflowLifecycleTools);
        Assert.NotNull(typeInfo.GetCustomAttribute<McpServerToolTypeAttribute>());

        var method = typeInfo.GetMethod(methodName);
        Assert.NotNull(method);

        var toolAttribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(toolAttribute);
        Assert.Equal(expectedToolName, toolAttribute!.Name);

        var descriptionAttribute = method.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(descriptionAttribute);
        Assert.False(string.IsNullOrWhiteSpace(descriptionAttribute!.Description));
    }

    /// <summary>
    /// 通过 WithToolsFromAssembly 注册后，MCP server 应能发现 WorkflowQueryTools 和 WorkflowLifecycleTools 的全部工具。
    /// </summary>
    [Fact]
    public void AllWorkflowTools_AreDiscoveredViaWithToolsFromAssembly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<INodeRegistry>(_ => Mock.Of<INodeRegistry>());
        services.AddMcpServer()
            .WithToolsFromAssembly(typeof(WorkflowQueryTools).Assembly);

        var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<McpServerTool>().ToList();

        var toolNames = tools.Select(t => t.ProtocolTool.Name).ToList();
        Assert.Contains("get_workflow", toolNames);
        Assert.Contains("list_workflows", toolNames);
        Assert.Contains("validate_workflow", toolNames);
        Assert.Contains("confirm_workflow", toolNames);
        Assert.Contains("execute_workflow", toolNames);
    }

    // ── 辅助方法 ──────────────────────────────────────────────────

    private static WorkflowLifecycleTools CreateLifecycleTools(
        IWorkflowValidationService? validationService = null,
        IWorkflowService? workflowService = null,
        IExecutionService? executionService = null)
    {
        validationService ??= Mock.Of<IWorkflowValidationService>();
        workflowService ??= Mock.Of<IWorkflowService>();
        executionService ??= Mock.Of<IExecutionService>();
        return new WorkflowLifecycleTools(validationService, workflowService, executionService);
    }
}
