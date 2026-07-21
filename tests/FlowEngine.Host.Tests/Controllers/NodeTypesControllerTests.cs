using System.Net;
using System.Net.Http.Json;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowEngine.Host.Tests.Controllers;

public class NodeTypesControllerTests : HostIntegrationTestBase
{
    public NodeTypesControllerTests(FlowEngineWebApplicationFactory factory)
        : base(factory, builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<INodeRegistry>(new FakeNodeRegistry()));
            });
        })
    {
    }

    [Fact]
    public async Task GetAll_ReturnsNodeTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("nodetypes@example.com", ct);

        var response = await client.GetAsync("/api/v1/node-types", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<NodeTypeDescriptor>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    [Fact]
    public async Task GetAll_WithCategoryFilter_ReturnsFilteredNodeTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("nodetypes-filter@example.com", ct);

        var response = await client.GetAsync("/api/v1/node-types?category=Core", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<NodeTypeDescriptor>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Start", result!.First().TypeName);
    }

    private sealed class FakeNodeRegistry : INodeRegistry
    {
        private readonly List<NodeTypeDescriptor> _descriptors = new()
        {
            new()
            {
                TypeName = "Start",
                DisplayName = "Start",
                Category = "Core",
                ExecutionMode = ExecutionMode.OnceForAll,
                    Parameters = [],
                    Ports = [],
                },
                new()
                {
                    TypeName = "HttpRequest",
                    DisplayName = "HTTP Request",
                    Category = "Integration",
                    ExecutionMode = ExecutionMode.OnceForAll,
                    Parameters =
                    [
                        new ParameterDefinition
                        {
                            Name = "Url",
                            DisplayName = "URL",
                            Type = ParameterType.String,
                        Options = [],
                    },
                ],
                Ports = [],
            },
        };

        public void Register(INodeType nodeType) => throw new NotImplementedException();

        public INodeType Get(string typeName) => throw new NotImplementedException();

        public bool TryGet(string typeName, out INodeType? nodeType)
        {
            nodeType = null;
            return false;
        }

        public IReadOnlyCollection<INodeType> GetAll() => throw new NotImplementedException();

        public INodeType CreateInstance(string typeName) => throw new NotImplementedException();

        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => _descriptors;

        public NodeTypeDescriptor GetDescriptor(string typeName)
            => _descriptors.First(d => d.TypeName == typeName);
    }
}
