using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
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
                new CredentialTestNode(),
                new FailingTestNode(),
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
    public async Task DryRunAsync_WithNodesAndConnections_ReturnsDryRunCompleted()
    {
        var request = CreateSetNodeRequest();

        var service = CreateService();
        var result = await service.DryRunAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.DryRunCompleted), result.Status);
        Assert.Single(result.NodeRecords);
        Assert.True(result.NodeRecords[0].Output is not null);
    }

    [Fact]
    public async Task DryRunAsync_PureComputationChain_PropagatesData()
    {
        var request = CreateSetAndFilterRequest();

        var service = CreateService();
        var result = await service.DryRunAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.DryRunCompleted), result.Status);
        Assert.Equal(2, result.NodeRecords.Count);
        Assert.All(result.NodeRecords, r => Assert.Equal("Completed", r.Status));
    }

    [Fact]
    public async Task DryRunAsync_WithTemporaryCredentials_ResolvesByName()
    {
        var request = CreateCredentialNodeRequest();

        var service = CreateService();
        var result = await service.DryRunAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.DryRunCompleted), result.Status);
        Assert.Single(result.NodeRecords);
        var output = (JsonNode?)result.NodeRecords[0].Output;
        Assert.NotNull(output);
        var token = output["output"]?["items"]?[0]?["data"]?["token"];
        Assert.NotNull(token);
        Assert.Equal("secret-token", token.GetValue<string>());
    }

    [Fact]
    public async Task DryRunAsync_FailingNode_ReturnsFailed()
    {
        var request = CreateFailingNodeRequest();

        var service = CreateService();
        var result = await service.DryRunAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.Failed), result.Status);
        Assert.Single(result.NodeRecords);
        Assert.Equal("Failed", result.NodeRecords[0].Status);
    }

    [Fact]
    public async Task DryRunAsync_DoesNotPersistWorkflowOrExecution()
    {
        var request = CreateSetNodeRequest();
        var workflowCountBefore = await _dbContext.Workflows.CountAsync(TestContext.Current.CancellationToken);
        var executionCountBefore = await _dbContext.ExecutionRecords.CountAsync(TestContext.Current.CancellationToken);

        var service = CreateService();
        var result = await service.DryRunAsync(request, TestContext.Current.CancellationToken);

        var workflowCountAfter = await _dbContext.Workflows.CountAsync(TestContext.Current.CancellationToken);
        var executionCountAfter = await _dbContext.ExecutionRecords.CountAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(workflowCountBefore, workflowCountAfter);
        Assert.Equal(executionCountBefore, executionCountAfter);
    }

    private WorkflowDryRunService CreateService()
    {
        return new WorkflowDryRunService(
            _nodeRegistry,
            _contextFactory,
            NullLogger<WorkflowDryRunService>.Instance);
    }

    private static DryRunWorkflowRequestDto CreateSetNodeRequest()
    {
        return new DryRunWorkflowRequestDto
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "set-1",
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

    private static DryRunWorkflowRequestDto CreateSetAndFilterRequest()
    {
        return new DryRunWorkflowRequestDto
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "set-1",
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
                new NodeDefinitionDto
                {
                    Id = "filter-1",
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
                new ConnectionDto
                {
                    Id = "conn-1",
                    SourceNodeId = "set-1",
                    SourcePortName = "output",
                    TargetNodeId = "filter-1",
                    TargetPortName = "input"
                }
            ]
        };
    }

    private static DryRunWorkflowRequestDto CreateCredentialNodeRequest()
    {
        return new DryRunWorkflowRequestDto
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "cred-1",
                    TypeName = "credentialTest",
                    Name = "CredentialTest",
                    IsEntry = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["credential"] = "my-api-key"
                    },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
                    ]
                }
            ],
            Connections = [],
            Credentials =
            [
                new DryRunCredentialDto
                {
                    Name = "my-api-key",
                    Type = "apiKey",
                    Fields = new Dictionary<string, string> { ["token"] = "secret-token" }
                }
            ]
        };
    }

    private static DryRunWorkflowRequestDto CreateFailingNodeRequest()
    {
        return new DryRunWorkflowRequestDto
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "fail-1",
                    TypeName = "failingTest",
                    Name = "FailingTest",
                    IsEntry = true,
                    Parameters = [],
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

    private sealed class FakeCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue { Fields = [] });

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }

    private sealed class CredentialTestNode : INodeType
    {
        public string TypeName => "credentialTest";
        public string DisplayName => "Credential Test";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
        ];

        public bool DefaultIsEntry => false;

        [Credential("apiKey")]
        public CredentialValue? Credential { get; set; }

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            var token = Credential?.Fields.GetValueOrDefault("token") ?? string.Empty;
            var output = new JsonObject { ["token"] = token };
            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch
                {
                    Items = [new DataItem { Data = output, Success = true, SourceIndex = 0 }]
                }
            });
        }
    }

    private sealed class FailingTestNode : INodeType
    {
        public string TypeName => "failingTest";
        public string DisplayName => "Failing Test";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
        ];

        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Dry-run failure simulation.");
        }
    }
}
