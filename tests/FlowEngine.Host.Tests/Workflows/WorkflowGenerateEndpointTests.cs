using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Application.Identity;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlowEngine.Host.Tests.Workflows;

/// <summary>
/// 工作流生成端点集成测试：验证鉴权（Write 权限）、LLM Mock 下生成并返回合法草案。
/// </summary>
public class WorkflowGenerateEndpointTests : IClassFixture<FlowEngineWebApplicationFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    public WorkflowGenerateEndpointTests(FlowEngineWebApplicationFactory factory)
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
            builder.UseSetting("ExecutionCleanup:Enabled", "false");
            builder.UseSetting("Audit:LogPath", auditDirectory);
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<INodeRegistry>(new FakeNodeRegistry(
                [
                    new NodeTypeDescriptor
                    {
                        TypeName = "trigger",
                        Ports = [new PortDefinition { Name = "out", Direction = PortDirection.Output }],
                    },
                    new NodeTypeDescriptor
                    {
                        TypeName = "sink",
                        Ports = [new PortDefinition { Name = "in", Direction = PortDirection.Input }],
                    },
                ])));
                services.Replace(ServiceDescriptor.Singleton<ICredentialAccessor>(new FakeCredentialAccessor()));
                services.Replace(ServiceDescriptor.Singleton<ILlmClient>(new FakeLlmClient(ValidDraft)));
                services.Replace(ServiceDescriptor.Singleton<IScheduleManager, NoOpScheduleManager>());
                services.RemoveAll<IHostedService>();
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
    public async Task Generate_WithEditorRole_ReturnsOkAndValidDraft()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-generate-editor@example.com", roles: ["Editor"], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/generate",
            new { description = "生成一个工作流" },
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonNode.Parse(json)!;
        Assert.True(doc["valid"]!.GetValue<bool>());
        Assert.NotNull(doc["draft"]);
        Assert.Equal(1, doc["attempts"]!.GetValue<int>());
    }

    [Fact]
    public async Task Generate_MissingDescription_ReturnsOkWithInvalid()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-generate-empty@example.com", roles: ["Editor"], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/generate",
            new { description = "   " },
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonNode.Parse(json)!;
        Assert.False(doc["valid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Generate_WithoutAuthentication_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/generate",
            new { description = "生成一个工作流" },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Generate_WithViewerRole_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-generate-viewer@example.com", roles: ["Viewer"], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/generate",
            new { description = "生成一个工作流" },
            ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private const string ValidDraft = """
    {
      "name": "gen",
      "nodes": [
        { "id": "n1", "typeName": "trigger", "isEntry": true },
        { "id": "n2", "typeName": "sink" }
      ],
      "connections": [
        { "sourceNodeId": "n1", "sourcePortName": "out", "targetNodeId": "n2", "targetPortName": "in" }
      ]
    }
    """;

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, IReadOnlyList<string>? roles = null, CancellationToken ct = default)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = new User
        {
            Email = email,
            UserName = email.Split('@')[0],
            DisplayName = email,
            PasswordHash = passwordHasher.HashPassword("StrongP@ss1"),
            IsActive = true,
        };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(ct);

        if (roles is not null)
        {
            foreach (var role in roles)
            {
                dbContext.Set<UserRole>().Add(new UserRole { UserId = user.Id, Role = role });
            }

            await dbContext.SaveChangesAsync(ct);
        }

        var token = tokenService.GenerateAccessToken(user.Id, user.Email, roles ?? []);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class FakeLlmClient : ILlmClient
    {
        private readonly string _content;

        public FakeLlmClient(string content) => _content = content;

        public Task<LlmResponse> ChatAsync(
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = _content });
    }

    private sealed class FakeNodeRegistry : INodeRegistry
    {
        private readonly IReadOnlyCollection<NodeTypeDescriptor> _descriptors;

        public FakeNodeRegistry(IReadOnlyCollection<NodeTypeDescriptor> descriptors)
            => _descriptors = descriptors;

        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => _descriptors;

        public void Register(INodeType nodeType) => throw new NotSupportedException();
        public INodeType Get(string typeName) => throw new NotSupportedException();
        public bool TryGet(string typeName, out INodeType? nodeType)
        {
            nodeType = null;
            return false;
        }
        public IReadOnlyCollection<INodeType> GetAll() => throw new NotSupportedException();
        public INodeType CreateInstance(string typeName) => throw new NotSupportedException();
        public NodeTypeDescriptor GetDescriptor(string typeName) => throw new NotSupportedException();
    }

    private sealed class FakeCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue
            {
                Name = "x",
                Type = "apiKey",
                Fields = new Dictionary<string, string>(),
                BinaryFields = new Dictionary<string, byte[]>(),
            });

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }
}
