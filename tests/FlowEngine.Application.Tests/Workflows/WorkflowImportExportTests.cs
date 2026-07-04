using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Application.Tests.Workflows;

public sealed class WorkflowImportExportTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public void ExportResult_ProducesValidJson()
    {
        var export = new WorkflowExportResult
        {
            Name = "Test Workflow",
            Version = 1,
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "node-1",
                    TypeName = "start",
                    Name = "Start",
                    IsEntry = true,
                    Ports =
                    [
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections = [],
            ExportedAt = DateTime.UtcNow,
            ExportedBy = "test-user",
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<WorkflowExportResult>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("Test Workflow", deserialized.Name);
        Assert.Equal(1, deserialized.Version);
        Assert.Single(deserialized.Nodes);
        Assert.Equal("start", deserialized.Nodes[0].TypeName);
        Assert.Empty(deserialized.Connections);
    }

    [Fact]
    public async Task Import_InvalidJson_ReturnsValidationError()
    {
        var registry = new StubNodeRegistry([]);
        var service = CreateImportService(registry);

        var result = await service.ImportAsync("not valid json", null, "user", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.ErrorType == "Validation");
    }

    [Fact]
    public async Task Import_EmptyJson_ReturnsValidationError()
    {
        var registry = new StubNodeRegistry([]);
        var service = CreateImportService(registry);

        var result = await service.ImportAsync("{}", null, "user", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.ErrorType == "Validation");
    }

    [Fact]
    public async Task Import_UnregisteredNodeType_ReturnsNodeNotFoundError()
    {
        var registry = new StubNodeRegistry([]);
        var service = CreateImportService(registry);

        var export = new WorkflowExportResult
        {
            Name = "Test",
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "n1",
                    TypeName = "unknown_type",
                    Name = "Node",
                },
            ],
        };
        var json = JsonSerializer.Serialize(export, JsonOptions);

        var result = await service.ImportAsync(json, null, "user", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.ErrorType == "NodeNotFound");
    }

    [Fact]
    public async Task Import_InvalidPortName_ReturnsPortNotFoundError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("start", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
        ]);
        var service = CreateImportService(registry);

        var export = new WorkflowExportResult
        {
            Name = "Test",
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "n1",
                    TypeName = "start",
                    Name = "Start",
                    Ports =
                    [
                        new PortInstance { Name = "nonexistent_port", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
        };
        var json = JsonSerializer.Serialize(export, JsonOptions);

        var result = await service.ImportAsync(json, null, "user", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.ErrorType == "PortNotFound");
    }

    [Fact]
    public async Task Import_DanglingConnection_ReturnsConnectionError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("start", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
        ]);
        var service = CreateImportService(registry);

        var export = new WorkflowExportResult
        {
            Name = "Test",
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "n1",
                    TypeName = "start",
                    Name = "Start",
                },
            ],
            Connections =
            [
                new ConnectionDto
                {
                    Id = "c1",
                    SourceNodeId = "n1",
                    SourcePortName = "output",
                    TargetNodeId = "nonexistent",
                    TargetPortName = "input",
                },
            ],
        };
        var json = JsonSerializer.Serialize(export, JsonOptions);

        var result = await service.ImportAsync(json, null, "user", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.ErrorType == "ConnectionError");
    }

    [Fact]
    public async Task ImportBatch_MixedValidAndInvalid_ReturnsPartialSuccess()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("start", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
        ]);
        var service = CreateImportService(registry);

        var validExport = new WorkflowExportResult
        {
            Name = "Valid",
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "n1",
                    TypeName = "start",
                    Name = "Start",
                },
            ],
        };
        var invalidExport = new WorkflowExportResult
        {
            Name = "Invalid",
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "n2",
                    TypeName = "unknown",
                    Name = "Bad Node",
                },
            ],
        };
        var batch = new List<WorkflowExportResult> { validExport, invalidExport };
        var json = JsonSerializer.Serialize(batch, JsonOptions);

        var result = await service.ImportBatchAsync(json, null, "user", TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(2, result.Results.Count);
        Assert.True(result.Results[0].Success);
        Assert.False(result.Results[1].Success);
    }

    [Fact]
    public async Task ImportBatch_EmptyArray_ReturnsFailure()
    {
        var registry = new StubNodeRegistry([]);
        var service = CreateImportService(registry);

        var result = await service.ImportBatchAsync("[]", null, "user", TestContext.Current.CancellationToken);

        Assert.Equal(0, result.SuccessCount);
        Assert.True(result.FailureCount >= 1);
    }

    private static WorkflowImportService CreateImportService(INodeRegistry registry)
    {
        return new WorkflowImportService(
            null!,
            registry,
            null!,
            null!);
    }

    private static NodeTypeDescriptor CreateDescriptor(
        string typeName,
        List<PortDefinition>? ports = null)
    {
        return new NodeTypeDescriptor
        {
            TypeName = typeName,
            DisplayName = typeName,
            Category = "Test",
            Ports = ports ?? [],
        };
    }

    private sealed class StubNodeRegistry(IReadOnlyCollection<NodeTypeDescriptor> descriptors) : INodeRegistry
    {
        public void Register(INodeType nodeType) { }
        public INodeType Get(string typeName) => throw new InvalidOperationException();
        public bool TryGet(string typeName, out INodeType? nodeType) { nodeType = null; return false; }
        public IReadOnlyCollection<INodeType> GetAll() => [];
        public INodeType CreateInstance(string typeName) => throw new InvalidOperationException();
        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => descriptors;
        public NodeTypeDescriptor GetDescriptor(string typeName) =>
            descriptors.First(d => d.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }
}
