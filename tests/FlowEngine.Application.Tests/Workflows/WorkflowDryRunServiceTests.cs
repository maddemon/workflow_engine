using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Workflows;
using FlowEngine.Core;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Application.Tests.Workflows;

public sealed class WorkflowDryRunServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly NodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;

    public WorkflowDryRunServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);

        _nodeRegistry = new NodeRegistry(
            new FlowEngine.Core.Abstractions.INodeType[]
            {
                new FlowEngine.Plugins.Standard.SetNode(),
                new FlowEngine.Plugins.Standard.MergeNode(),
                new FlowEngine.Plugins.Standard.IfNode(),
                new FlowEngine.Plugins.Standard.SwitchNode(),
                new FlowEngine.Plugins.Standard.CalculatorToolNode(),
                new FlowEngine.Plugins.Standard.FilterNode(),
                new FlowEngine.Plugins.Standard.SortNode(),
                new FlowEngine.Plugins.Standard.LimitNode(),
                new FlowEngine.Plugins.Standard.AggregateNode(),
                new FlowEngine.Plugins.Standard.HttpRequestNode(),
            },
            NullLogger<NodeRegistry>.Instance);

        _contextFactory = new NodeExecutionContextFactory(
            _nodeRegistry,
            new FlowEngine.Runtime.Expressions.ParameterResolver(NullLogger<FlowEngine.Runtime.Expressions.ParameterResolver>.Instance),
            new FakeCredentialAccessor(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            NullLogger<ParameterHydrator>.Instance,
            NullLogger<JsEngine>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task DryRunAsync_NonExistingWorkflow_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.DryRunAsync(Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DryRunAsync_SupportsDryRunNode_ExecutesAndReturnsOutput()
    {
        var workflow = CreateWorkflowWithSetNode();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService();
        var result = await service.DryRunAsync(workflow.Id, new JsonObject { ["value"] = 1 }, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result!.NodeRecords);
        var record = result.NodeRecords[0];
        Assert.False(record.Skipped);
        Assert.True(record.Success);
        Assert.Equal("set", record.NodeType);
        Assert.NotNull(record.Output);
    }

    [Fact]
    public async Task DryRunAsync_NonDryRunNode_SkipsWithWarning()
    {
        var workflow = CreateWorkflowWithHttpNode();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService();
        var result = await service.DryRunAsync(workflow.Id, null, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result!.NodeRecords);
        var record = result.NodeRecords[0];
        Assert.True(record.Skipped);
        Assert.Contains("httpRequest", record.SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DryRunAsync_PureComputationChain_PropagatesData()
    {
        var workflow = CreateWorkflowWithSetAndFilterNodes();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService();
        var result = await service.DryRunAsync(workflow.Id, new JsonObject { ["value"] = 5 }, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result!.NodeRecords.Count);
        Assert.All(result.NodeRecords, r => Assert.True(r.Success, $"Node {r.NodeName} failed: {(r.Output as DataBatch)?.Items.FirstOrDefault()?.Error?.Message}\n{(r.Output as DataBatch)?.Items.FirstOrDefault()?.Error?.StackTrace}"));
        Assert.All(result.NodeRecords, r => Assert.False(r.Skipped));
    }

    private WorkflowDryRunService CreateService()
    {
        return new WorkflowDryRunService(
            _dbContext,
            _nodeRegistry,
            _contextFactory,
            NullLogger<WorkflowDryRunService>.Instance);
    }

    private static Workflow CreateWorkflowWithSetNode()
    {
        var nodeId = Guid.NewGuid();
        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "DryRun Set",
            CreatedBy = "test",
            IsActive = true,
            Nodes =
            [
                new NodeDefinition
                {
                    Id = nodeId,
                    TypeName = "set",
                    Name = "Set",
                    IsEntry = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["fields"] = JsonSerializer.SerializeToNode(new[] { new { name = "greeting", value = "hello" } }, JsonDefaults.Options)!,
                        ["include"] = "All"
                    },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
                    ]
                }
            ],
            Connections = []
        };
    }

    private static Workflow CreateWorkflowWithHttpNode()
    {
        var nodeId = Guid.NewGuid();
        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "DryRun Http",
            CreatedBy = "test",
            IsActive = true,
            Nodes =
            [
                new NodeDefinition
                {
                    Id = nodeId,
                    TypeName = "httpRequest",
                    Name = "HTTP",
                    IsEntry = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["url"] = "https://example.com",
                        ["method"] = "GET"
                    },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
                    ]
                }
            ],
            Connections = []
        };
    }

    private static Workflow CreateWorkflowWithSetAndFilterNodes()
    {
        var setId = Guid.NewGuid();
        var filterId = Guid.NewGuid();
        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "DryRun Chain",
            CreatedBy = "test",
            IsActive = true,
            Nodes =
            [
                new NodeDefinition
                {
                    Id = setId,
                    TypeName = "set",
                    Name = "Set",
                    IsEntry = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["fields"] = JsonSerializer.SerializeToNode(new[] { new { name = "score", value = "10" } }, JsonDefaults.Options)!,
                        ["include"] = "All"
                    },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
                    ]
                },
                new NodeDefinition
                {
                    Id = filterId,
                    TypeName = "filter",
                    Name = "Filter",
                    Parameters = new Dictionary<string, object>
                    {
                        ["condition"] = "true"
                    },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "kept", Direction = PortDirection.Output, Type = PortType.Main }
                    ]
                }
            ],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = setId,
                    SourcePortName = "output",
                    TargetNodeId = filterId,
                    TargetPortName = "input"
                }
            ]
        };
    }

    private sealed class FakeCredentialAccessor : FlowEngine.Core.Abstractions.ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue { Fields = [] });
    }
}
