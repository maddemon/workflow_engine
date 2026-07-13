using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using FlowEngine.Application.Dtos;
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
/// Workflow 草稿 MCP 工具单元测试。
/// </summary>
public class WorkflowToolsTests
{
    // ── assemble_workflow 成功路径 ────────────────────────────────

    /// <summary>
    /// assemble_workflow 在合法输入时应返回 AssembleWorkflowResult。
    /// </summary>
    [Fact]
    public async Task AssembleWorkflow_ValidInput_ReturnsResult()
    {
        var expectedResult = new AssembleWorkflowResult
        {
            DraftId = Guid.NewGuid(),
            Workflow = new WorkflowDto
            {
                Id = Guid.NewGuid(),
                Name = "test-flow",
                Nodes = [],
                Connections = [],
            },
        };

        var assemblyMock = new Mock<IWorkflowAssemblyService>();
        assemblyMock
            .Setup(s => s.AssembleAsync(It.IsAny<AssembleWorkflowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var tools = CreateTools(assemblyMock.Object);

        var result = await tools.AssembleWorkflow(
            name: "test-flow",
            nodes: [new AiDraftNodeDto { Id = "fetch", TypeName = "httpRequest" }],
            projectId: null,
            connections: null,
            cancellationToken: CancellationToken.None);

        Assert.Same(expectedResult, result);
    }

    /// <summary>
    /// assemble_workflow 在合法输入含 projectId 和 connections 时应正确转发。
    /// </summary>
    [Fact]
    public async Task AssembleWorkflow_WithProjectIdAndConnections_PassesThemThrough()
    {
        var projectId = Guid.NewGuid();
        AssembleWorkflowRequest? captured = null;

        var assemblyMock = new Mock<IWorkflowAssemblyService>();
        assemblyMock
            .Setup(s => s.AssembleAsync(It.IsAny<AssembleWorkflowRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AssembleWorkflowRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new AssembleWorkflowResult
            {
                DraftId = Guid.NewGuid(),
                Workflow = new WorkflowDto { Id = Guid.NewGuid(), Name = "flow" },
            });

        var tools = CreateTools(assemblyMock.Object);

        var nodes = new List<AiDraftNodeDto>
        {
            new() { Id = "trigger", TypeName = "manualTrigger" },
            new() { Id = "fetch", TypeName = "httpRequest" },
        };
        var connections = new List<AiDraftConnectionDto>
        {
            new() { From = "trigger", To = "fetch" },
        };

        await tools.AssembleWorkflow(
            name: "my-flow",
            nodes: nodes,
            projectId: projectId.ToString(),
            connections: connections,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("my-flow", captured!.Name);
        Assert.Equal(projectId, captured.ProjectId);
        Assert.Equal(2, captured.Nodes.Count);
        Assert.Single(captured.Connections);
    }

    // ── assemble_workflow 输入校验 ─────────────────────────────────

    /// <summary>
    /// assemble_workflow 在 name 为空/空白时应返回 InvalidInput 错误。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AssembleWorkflow_EmptyName_ReturnsInvalidInputError(string? name)
    {
        var tools = CreateTools();
        var result = await tools.AssembleWorkflow(
            name: name!,
            nodes: [new AiDraftNodeDto { TypeName = "x" }],
            cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
        Assert.Contains("名称", element.GetProperty("message").GetString());
    }

    /// <summary>
    /// assemble_workflow 在 nodes 为 null 时应返回 InvalidInput 错误。
    /// </summary>
    [Fact]
    public async Task AssembleWorkflow_NullNodes_ReturnsInvalidInputError()
    {
        var tools = CreateTools();
        var result = await tools.AssembleWorkflow(
            name: "flow",
            nodes: null!,
            cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// assemble_workflow 在 nodes 为空列表时应返回 InvalidInput 错误。
    /// </summary>
    [Fact]
    public async Task AssembleWorkflow_EmptyNodes_ReturnsInvalidInputError()
    {
        var tools = CreateTools();
        var result = await tools.AssembleWorkflow(
            name: "flow",
            nodes: [],
            cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    // ── assemble_workflow BusinessException 捕获 ──────────────────

    /// <summary>
    /// assemble_workflow 在服务抛出 BusinessException 时应返回 AssembleFailed 结构化错误，不泄漏异常。
    /// </summary>
    [Fact]
    public async Task AssembleWorkflow_BusinessException_ReturnsAssembleFailedError()
    {
        var assemblyMock = new Mock<IWorkflowAssemblyService>();
        assemblyMock
            .Setup(s => s.AssembleAsync(It.IsAny<AssembleWorkflowRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("节点类型不存在"));

        var tools = CreateTools(assemblyMock.Object);
        var result = await tools.AssembleWorkflow(
            name: "flow",
            nodes: [new AiDraftNodeDto { TypeName = "unknown" }],
            cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("AssembleFailed", element.GetProperty("errorCode").GetString());
        Assert.Contains("节点类型不存在", element.GetProperty("message").GetString());
    }

    // ── modify_workflow 成功路径 ──────────────────────────────────

    /// <summary>
    /// modify_workflow 在合法输入时应返回 ModifyWorkflowResult。
    /// </summary>
    [Fact]
    public async Task ModifyWorkflow_ValidInput_ReturnsResult()
    {
        var workflowId = Guid.NewGuid();
        var expectedResult = new ModifyWorkflowResult
        {
            DraftId = Guid.NewGuid(),
            Workflow = new WorkflowDto { Id = Guid.NewGuid(), Name = "modified" },
            Diff = [new StructuredDiff { Op = "add", NodeId = "newNode", After = "newNode" }],
        };

        var modificationMock = new Mock<IWorkflowModificationService>();
        modificationMock
            .Setup(s => s.ModifyAsync(It.IsAny<Guid>(), It.IsAny<ModifyWorkflowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var tools = CreateTools(modificationService: modificationMock.Object);

        var result = await tools.ModifyWorkflow(
            workflowId: workflowId.ToString(),
            operations: [new WorkflowOperation { Op = "add" }],
            cancellationToken: CancellationToken.None);

        Assert.Same(expectedResult, result);
    }

    // ── modify_workflow 输入校验 ───────────────────────────────────

    /// <summary>
    /// modify_workflow 在 workflowId 为空/空白时应返回 InvalidInput 错误。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ModifyWorkflow_InvalidWorkflowId_ReturnsInvalidInputError(string? workflowId)
    {
        var tools = CreateTools();
        var result = await tools.ModifyWorkflow(
            workflowId: workflowId!,
            operations: [new WorkflowOperation { Op = "add" }],
            cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// modify_workflow 在 workflowId 为 Guid.Empty 时应返回 InvalidInput 错误。
    /// </summary>
    [Fact]
    public async Task ModifyWorkflow_EmptyGuid_ReturnsInvalidInputError()
    {
        var tools = CreateTools();
        var result = await tools.ModifyWorkflow(
            workflowId: Guid.Empty.ToString(),
            operations: [new WorkflowOperation { Op = "add" }],
            cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// modify_workflow 在 workflowId 不是合法 Guid 格式时应返回 InvalidInput 错误。
    /// </summary>
    [Fact]
    public async Task ModifyWorkflow_InvalidGuidFormat_ReturnsInvalidInputError()
    {
        var tools = CreateTools();
        var result = await tools.ModifyWorkflow(
            workflowId: "not-a-guid",
            operations: [new WorkflowOperation { Op = "add" }],
            cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// modify_workflow 在 operations 为 null 时应返回 InvalidInput 错误。
    /// </summary>
    [Fact]
    public async Task ModifyWorkflow_NullOperations_ReturnsInvalidInputError()
    {
        var tools = CreateTools();
        var result = await tools.ModifyWorkflow(
            workflowId: Guid.NewGuid().ToString(),
            operations: null!,
            cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// modify_workflow 在 operations 为空列表时应返回 InvalidInput 错误。
    /// </summary>
    [Fact]
    public async Task ModifyWorkflow_EmptyOperations_ReturnsInvalidInputError()
    {
        var tools = CreateTools();
        var result = await tools.ModifyWorkflow(
            workflowId: Guid.NewGuid().ToString(),
            operations: [],
            cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidInput", element.GetProperty("errorCode").GetString());
    }

    // ── modify_workflow BusinessException 捕获 ─────────────────────

    /// <summary>
    /// modify_workflow 在服务抛出 BusinessException 时应返回 ModifyFailed 结构化错误，不泄漏异常。
    /// </summary>
    [Fact]
    public async Task ModifyWorkflow_BusinessException_ReturnsModifyFailedError()
    {
        var modificationMock = new Mock<IWorkflowModificationService>();
        modificationMock
            .Setup(s => s.ModifyAsync(It.IsAny<Guid>(), It.IsAny<ModifyWorkflowRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("工作流不存在"));

        var tools = CreateTools(modificationService: modificationMock.Object);
        var result = await tools.ModifyWorkflow(
            workflowId: Guid.NewGuid().ToString(),
            operations: [new WorkflowOperation { Op = "remove", Path = "/nodes/fetch" }],
            cancellationToken: CancellationToken.None);

        var element = JsonSerializer.SerializeToElement(result);
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("ModifyFailed", element.GetProperty("errorCode").GetString());
        Assert.Contains("工作流不存在", element.GetProperty("message").GetString());
    }

    // ── MCP 工具注册验证 ──────────────────────────────────────────

    /// <summary>
    /// WorkflowTools 应被 MCP SDK 的工具扫描机制识别，并注册指定名称的工具。
    /// </summary>
    [Theory]
    [InlineData(nameof(WorkflowTools.AssembleWorkflow), "assemble_workflow")]
    [InlineData(nameof(WorkflowTools.ModifyWorkflow), "modify_workflow")]
    public void ToolMethods_AreDiscoveredWithExpectedNames(string methodName, string expectedToolName)
    {
        var typeInfo = typeof(WorkflowTools);
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
    /// 通过 WithToolsFromAssembly 注册后，MCP server 应能发现 WorkflowTools 的两个工具。
    /// </summary>
    [Fact]
    public void WorkflowTools_AreDiscoveredViaWithToolsFromAssembly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<INodeRegistry>(_ => Mock.Of<INodeRegistry>());
        services.AddSingleton<WorkflowService>();
        services.AddSingleton<WorkflowValidator>();
        services.AddScoped<IWorkflowAssemblyService, WorkflowAssemblyService>();
        services.AddScoped<IWorkflowModificationService, WorkflowModificationService>();
        services.AddMcpServer()
            .WithToolsFromAssembly(typeof(WorkflowTools).Assembly);

        var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<McpServerTool>().ToList();

        var toolNames = tools.Select(t => t.ProtocolTool.Name).ToList();
        Assert.Contains("assemble_workflow", toolNames);
        Assert.Contains("modify_workflow", toolNames);
    }

    // ── 辅助方法 ──────────────────────────────────────────────────

    private static WorkflowTools CreateTools(
        IWorkflowAssemblyService? assemblyService = null,
        IWorkflowModificationService? modificationService = null)
    {
        assemblyService ??= Mock.Of<IWorkflowAssemblyService>();
        modificationService ??= Mock.Of<IWorkflowModificationService>();
        return new WorkflowTools(assemblyService, modificationService);
    }
}
