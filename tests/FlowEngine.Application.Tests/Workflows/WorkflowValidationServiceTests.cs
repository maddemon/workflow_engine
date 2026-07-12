#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Workflows;

public sealed class WorkflowValidationServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly StubNodeRegistry _registry;
    private readonly WorkflowValidationService _service;

    public WorkflowValidationServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase($"ValTestDb_{Guid.NewGuid()}")
            .Options;
        _dbContext = new FlowEngineDbContext(options);

        _registry = new StubNodeRegistry(
        [
            new NodeTypeDescriptor
            {
                TypeName = "httpRequest",
                DisplayName = "HTTP Request",
                Category = "HTTP",
                Parameters =
                [
                    new ParameterDefinition { Name = "url", DisplayName = "URL", Type = ParameterType.String, Required = true },
                    new ParameterDefinition { Name = "method", DisplayName = "Method", Type = ParameterType.Options, Options = [new Option { Label = "GET", Value = "GET" }, new Option { Label = "POST", Value = "POST" }, new Option { Label = "PUT", Value = "PUT" }] },
                ],
                Ports =
                [
                    new PortDefinition { Name = "input", DisplayName = "输入", Direction = PortDirection.Input, Type = PortType.Main },
                    new PortDefinition { Name = "output", DisplayName = "输出", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
            new NodeTypeDescriptor
            {
                TypeName = "webhookTrigger",
                DisplayName = "Webhook Trigger",
                Category = "Trigger",
                DefaultIsEntry = true,
                Ports =
                [
                    new PortDefinition { Name = "output", DisplayName = "输出", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
            new NodeTypeDescriptor
            {
                TypeName = "transform",
                DisplayName = "Transform",
                Category = "Data",
                Ports =
                [
                    new PortDefinition { Name = "input", DisplayName = "输入", Direction = PortDirection.Input, Type = PortType.Main },
                    new PortDefinition { Name = "output", DisplayName = "输出", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
            // P5a：带默认值的必填参数（如枚举）本质上可选，AI 不填时使用默认值即可，不应判为缺失。
            new NodeTypeDescriptor
            {
                TypeName = "notify",
                DisplayName = "Notify",
                Category = "Notification",
                Parameters =
                [
                    new ParameterDefinition
                    {
                        Name = "channel", DisplayName = "渠道", Type = ParameterType.Options, Required = true,
                        DefaultValue = "email",
                        Options = [new Option { Label = "Email", Value = "email" }, new Option { Label = "Sms", Value = "sms" }],
                    },
                ],
                Ports =
                [
                    new PortDefinition { Name = "input", DisplayName = "输入", Direction = PortDirection.Input, Type = PortType.Main },
                    new PortDefinition { Name = "output", DisplayName = "输出", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
        ]);

        _service = new WorkflowValidationService(_registry, _dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ValidateAsync_ValidWorkflow_Passes()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger",
                    Ports = [new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }],
                },
                new NodeDefinitionDto
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
                new ConnectionDto { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "fetch", TargetPortName = "input" },
            ],
        };

        var result = await _service.ValidateAsync(request);

        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
        Assert.True(result.CanAutoFix); // 合法工作流默认可自动修复
    }

    [Fact]
    public async Task ValidateAsync_MissingRequiredParameter_ReturnsError()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger",
                    Ports = [new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }],
                },
                new NodeDefinitionDto
                {
                    Id = "fetch", TypeName = "httpRequest", Name = "Fetch",
                    // Missing required "url" parameter
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections =
            [
                new ConnectionDto { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "fetch", TargetPortName = "input" },
            ],
        };

        var result = await _service.ValidateAsync(request);

        Assert.False(result.Valid);
        var error = result.Errors.First(e => e.ErrorType == "MissingRequired" && e.NodeId == "fetch");
        Assert.False(string.IsNullOrEmpty(error.SuggestedFix)); // 结构化错误含修复建议
        Assert.True(result.CanAutoFix); // MissingRequired 可自动修复
    }

    [Fact]
    public async Task ValidateAsync_CyclicDependency_ReturnsError()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "A", TypeName = "transform", Name = "A",
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
                new NodeDefinitionDto
                {
                    Id = "B", TypeName = "transform", Name = "B",
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections =
            [
                new ConnectionDto { SourceNodeId = "A", SourcePortName = "output", TargetNodeId = "B", TargetPortName = "input" },
                new ConnectionDto { SourceNodeId = "B", SourcePortName = "output", TargetNodeId = "A", TargetPortName = "input" },
            ],
        };

        var result = await _service.ValidateAsync(request);

        Assert.False(result.Valid);
        var error = result.Errors.First(e => e.ErrorType == "TopologyError" && e.Message.Contains("循环依赖"));
        Assert.False(string.IsNullOrEmpty(error.SuggestedFix));
        Assert.False(result.CanAutoFix); // 循环依赖不可自动修复
    }

    [Fact]
    public async Task ValidateAsync_NoTrigger_ReturnsError()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "fetch", TypeName = "httpRequest", Name = "Fetch",
                    Parameters = new() { ["url"] = "https://api.com" },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
        };

        var result = await _service.ValidateAsync(request);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.ErrorType == "MissingRequired" && e.Message.Contains("触发器"));
    }

    [Fact]
    public async Task ValidateAsync_ByWorkflowId_LoadedSuccessfully()
    {
        // Seed a valid workflow
        var workflow = new Workflow
        {
            Name = "Persisted Workflow",
            CreatedBy = "test",
            IsActive = true,
            Nodes =
            [
                new NodeDefinition { Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger" },
                new NodeDefinition
                {
                    Id = "fetch", TypeName = "httpRequest", Name = "Fetch",
                    Parameters = new() { ["url"] = "https://api.example.com" },
                },
            ],
            Connections =
            [
                new Connection { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "fetch", TargetPortName = "input" },
            ],
        };
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync();

        var request = new ValidateWorkflowRequest
        {
            WorkflowId = workflow.Id,
        };

        var result = await _service.ValidateAsync(request);
        Assert.True(result.Valid); // validation uses descriptors from registry, not node instance ports
    }

    [Fact]
    public async Task ValidateAsync_UnknownNodeType_ReturnsError()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "unknown", TypeName = "nonExistentType", Name = "Unknown",
                },
            ],
        };

        var result = await _service.ValidateAsync(request);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.ErrorType == "InvalidType" && e.NodeId == "unknown");
    }

    [Fact]
    public async Task ValidateAsync_EmptyNodes_ReturnsError()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes = [],
        };

        var result = await _service.ValidateAsync(request);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.ErrorType == "MissingRequired");
    }

    [Fact]
    public async Task ValidateAsync_DanglingConnection_ReturnsError()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger",
                    Ports = [new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }],
                },
            ],
            Connections =
            [
                new ConnectionDto { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "nonexistent", TargetPortName = "input" },
            ],
        };

        var result = await _service.ValidateAsync(request);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.ErrorType == "TopologyError" && e.Message.Contains("不存在"));
    }

    [Fact]
    public async Task ValidateAsync_InvalidOptionValue_ReturnsInvalidValueError()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger",
                    Ports = [new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }],
                },
                new NodeDefinitionDto
                {
                    Id = "fetch", TypeName = "httpRequest", Name = "Fetch",
                    Parameters = new() { ["url"] = "https://api.example.com", ["method"] = "DELETE" },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections =
            [
                new ConnectionDto { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "fetch", TargetPortName = "input" },
            ],
        };

        var result = await _service.ValidateAsync(request);

        Assert.False(result.Valid);
        var error = result.Errors.First(e => e.ErrorType == "InvalidValue" && e.NodeId == "fetch");
        Assert.Contains("DELETE", error.Message);
        Assert.Contains("GET", error.Message);
        Assert.Contains("POST", error.Message);
        Assert.False(string.IsNullOrEmpty(error.SuggestedFix));
        Assert.True(result.CanAutoFix); // InvalidValue 可自动修复
    }

    // P5a：必填参数若已配置默认值，节点缺失该参数时不报错（使用默认值即可）。
    [Fact]
    public async Task ValidateAsync_RequiredParameterWithDefault_MissingValue_Passes()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger",
                    Ports = [new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }],
                },
                new NodeDefinitionDto
                {
                    Id = "msg", TypeName = "notify", Name = "Notify",
                    // 缺失必填参数 channel，但该参数带有默认值 email
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections =
            [
                new ConnectionDto { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "msg", TargetPortName = "input" },
            ],
        };

        var result = await _service.ValidateAsync(request);

        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_ValidOptionValue_Passes()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger",
                    Ports = [new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }],
                },
                new NodeDefinitionDto
                {
                    Id = "fetch", TypeName = "httpRequest", Name = "Fetch",
                    Parameters = new() { ["url"] = "https://api.example.com", ["method"] = "POST" },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections =
            [
                new ConnectionDto { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "fetch", TargetPortName = "input" },
            ],
        };

        var result = await _service.ValidateAsync(request);

        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_OptionValue_CaseInsensitiveMatch_Passes()
    {
        var request = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger",
                    Ports = [new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }],
                },
                new NodeDefinitionDto
                {
                    Id = "fetch", TypeName = "httpRequest", Name = "Fetch",
                    Parameters = new() { ["url"] = "https://api.example.com", ["method"] = "post" },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections =
            [
                new ConnectionDto { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "fetch", TargetPortName = "input" },
            ],
        };

        var result = await _service.ValidateAsync(request);

        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
    }

    // ── Stubs ─────────────────────────────────────────────────

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
}
