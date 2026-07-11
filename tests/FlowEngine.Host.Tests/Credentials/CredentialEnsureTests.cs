using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlowEngine.Host.Tests.Credentials;

public class CredentialEnsureTests : IClassFixture<FlowEngineWebApplicationFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    public CredentialEnsureTests(FlowEngineWebApplicationFactory factory)
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
                services.Replace(ServiceDescriptor.Singleton<IScheduleManager, NoOpScheduleManager>());
                services.Replace(ServiceDescriptor.Singleton<ICryptoKeyProvider, TestCryptoKeyProvider>());
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
    public async Task Ensure_NewCredential_ReturnsCreated201()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-credential-ensure-create@example.com", [RoleConstants.Admin], ct);

        var dto = new CreateCredentialDto
        {
            Name = "Ensure API Key",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["apiKey"] = "sk-create" },
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials/ensure", dto, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CredentialDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal("Ensure API Key", result!.Name);
        Assert.Equal("sk-create", result.Fields["apiKey"]);
    }

    [Fact]
    public async Task GetTypes_ReturnsOkWithBuiltInCredentialTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-credential-types@example.com", [RoleConstants.Admin], ct);

        var response = await client.GetAsync("/api/v1/credentials/types", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var types = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonArray>(TestJsonOptions, ct);
        Assert.NotNull(types);
        Assert.Contains(types!, t => (string?)t!["name"] == "apiKey");
        Assert.Contains(types!, t => (string?)t!["name"] == "connectionString");
        Assert.Contains(types!, t => (string?)t!["name"] == "oauth2");
    }

    [Fact]
    public async Task Ensure_ExistingCredential_ReturnsOk200AndUpdatesFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "jwt-credential-ensure-update@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var credential = await SeedCredentialAsync(email, "Ensure Update Key", "apiKey", new Dictionary<string, string> { ["apiKey"] = "sk-old" }, ct);

        var dto = new CreateCredentialDto
        {
            Name = credential.Name,
            Type = credential.Type,
            Fields = new Dictionary<string, string> { ["apiKey"] = "sk-new" },
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials/ensure", dto, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CredentialDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(credential.Id, result!.Id);
        Assert.Equal("sk-new", result.Fields["apiKey"]);
    }

    [Fact]
    public async Task Ensure_Viewer_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-credential-ensure-viewer@example.com", [RoleConstants.Viewer], ct);

        var dto = new CreateCredentialDto
        {
            Name = "Ensure Key",
            Type = "apiKey",
            Fields = [],
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials/ensure", dto, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ensure_WithoutAuthentication_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var dto = new CreateCredentialDto
        {
            Name = "Ensure Key",
            Type = "apiKey",
            Fields = [],
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials/ensure", dto, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

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

    private async Task<Credential> SeedCredentialAsync(
        string email,
        string name,
        string type,
        Dictionary<string, string> fields,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = dbContext.Set<User>().First(u => u.Email == email);

        var credential = new Credential
        {
            Name = name,
            Type = type,
            Data = fields.ToDictionary(
                kvp => kvp.Key,
                kvp => new EncryptedField { CipherText = $"encrypted:{kvp.Value}", Nonce = "nonce", Tag = "tag" }),
            KeyVersion = "v1",
        };
        dbContext.Credentials.Add(credential);
        await dbContext.SaveChangesAsync(ct);
        return credential;
    }

    private static System.Text.Json.JsonSerializerOptions TestJsonOptions => new(System.Text.Json.JsonSerializerDefaults.Web);

    private sealed class TestCryptoKeyProvider : ICryptoKeyProvider
    {
        public byte[] GetKey() =>
            Convert.FromHexString("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");
    }
}
