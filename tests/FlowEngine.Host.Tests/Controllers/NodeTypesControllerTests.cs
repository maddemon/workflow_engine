using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlowEngine.Host.Tests.Controllers;

public class NodeTypesControllerTests : IClassFixture<FlowEngineWebApplicationFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    public NodeTypesControllerTests(FlowEngineWebApplicationFactory factory)
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "flowengine-tests", Guid.NewGuid().ToString());
        var dbDirectory = Path.Combine(_tempRoot, "db");
        var auditDirectory = Path.Combine(_tempRoot, "audit");
        Directory.CreateDirectory(dbDirectory);
        Directory.CreateDirectory(auditDirectory);

        var dbPath = Path.Combine(dbDirectory, "flowengine.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={dbPath};Mode=ReadWriteCreate");
            builder.UseSetting("Audit:LogPath", auditDirectory);
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IScheduleManager, NoOpScheduleManager>());
                services.RemoveAll<IHostedService>();
                services.Replace(ServiceDescriptor.Singleton<INodeRegistry>(new FakeNodeRegistry()));
            });
        });

        _factory.ClientOptions.BaseAddress = new Uri("http://localhost");
    }

    public void Dispose()
    {
        _factory.Dispose();
        try
        {
            Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // 忽略清理临时目录时的错误，不影响测试结果。
        }
    }

    [Fact]
    public async Task GetAll_ReturnsNodeTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

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
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/node-types?category=Core", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<NodeTypeDescriptor>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Start", result!.First().TypeName);
    }

    private static JsonSerializerOptions TestJsonOptions
    {
        get
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            return options;
        }
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
